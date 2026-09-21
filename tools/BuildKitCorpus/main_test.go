package main

import (
	"bytes"
	"encoding/base64"
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
)

func TestMetadataValidation(t *testing.T) {
	valid := `{"schemaVersion":1,"repository":"moby/buildkit","frontendVersion":"1.27.0","sourceCommit":"dddd5621af04ea57823085c93a063383f71d3173"}`
	for _, input := range []string{
		strings.Replace(valid, `"schemaVersion":1`, `"schemaVersion":2`, 1),
		strings.Replace(valid, "moby/buildkit", "other/repo", 1),
		strings.Replace(valid, "1.27.0", "1.27.0-labs", 1),
		strings.Replace(valid, "dddd5621af04ea57823085c93a063383f71d3173", "dddd562", 1),
		strings.Replace(valid, `"schemaVersion":1`, `"schemaVersion":1,"unknown":true`, 1),
		valid + "{}",
	} {
		if _, err := parseMetadata([]byte(input)); err == nil {
			t.Errorf("accepted invalid metadata: %s", input)
		}
	}
	if _, err := parseMetadata([]byte(valid)); err != nil {
		t.Fatal(err)
	}
}

func TestExactInstructionSpans(t *testing.T) {
	tests := []struct {
		name, source, input, escape string
		start, end                  int
	}{
		{"LF", "# comment\nFROM alpine\n", "FROM alpine\n", "\\", 2, 2},
		{"CRLF", "# comment\r\nRUN echo a \\\r\n # inside\r\n b\r\n", "RUN echo a \\\r\n # inside\r\n b\r\n", "\\", 2, 4},
		{"EOF", "FROM alpine", "FROM alpine", "\\", 1, 1},
		{"BOM", "\xef\xbb\xbfFROM alpine\n", "\xef\xbb\xbfFROM alpine\n", "\\", 1, 1},
		{"escape", "# escape=`\r\nRUN echo a `\r\n b\r\n", "RUN echo a `\r\n b\r\n", "`", 2, 3},
		{"heredoc", "RUN <<EOF\nhello\nEOF\n", "RUN <<EOF\nhello\nEOF\n", "\\", 1, 3},
		{"heredocCRLF", "RUN <<EOF\r\nhello\r\nEOF\r\n", "RUN <<EOF\r\nhello\r\nEOF\r\n", "\\", 1, 3},
		{"onbuildHeredoc", "ONBUILD RUN <<EOF\nhello\nEOF\n", "ONBUILD RUN <<EOF\nhello\nEOF\n", "\\", 1, 3},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			cases, _, err := extractPositive("test/Dockerfile", []byte(tt.source), nil)
			if err != nil {
				t.Fatal(err)
			}
			if len(cases) != 1 {
				t.Fatalf("got %d cases", len(cases))
			}
			c := cases[0]
			got, err := base64.StdEncoding.DecodeString(c.InputBase64)
			if err != nil || string(got) != tt.input || c.StartLine != tt.start || c.EndLine != tt.end || c.EscapeCharacter != tt.escape || c.InputSHA256 != digest([]byte(tt.input)) {
				t.Fatalf("wrong exact span: %+v, bytes %q", c, got)
			}
		})
	}
}

func TestGoldenAndEncodingRejection(t *testing.T) {
	if _, _, err := extractPositive("test", []byte("FROM alpine\n"), []byte("wrong\n")); err == nil {
		t.Fatal("accepted changed golden")
	}
	if _, _, err := extractPositive("test", []byte("RUN echo \xff\n"), nil); err == nil {
		t.Fatal("accepted non-UTF8 source")
	}
	for _, bounds := range [][2]int{{0, 1}, {2, 1}, {1, 3}, {2, 2}} {
		if _, err := sourceSlice([]byte("FROM alpine\n"), bounds[0], bounds[1]); err == nil {
			t.Fatalf("accepted invalid range %v", bounds)
		}
	}
}

func TestNegativeSelectors(t *testing.T) {
	for _, tt := range []struct {
		name, input, expected string
		start                 int
	}{
		{"env_no_value", "FROM busybox\n\nENV PATH\n", "ENV PATH\n", 3},
		{"shykes-nested-json", "CMD [ \"echo\", [ \"nested json\" ] ]\n", "CMD [ \"echo\", [ \"nested json\" ] ]\n", 1},
	} {
		path := parserRoot + "/testfiles-negative/" + tt.name + "/Dockerfile"
		cases, _, err := extractNegative(path, []byte(tt.input))
		if err != nil || len(cases) != 1 {
			t.Fatalf("negative %s: %v, %+v", tt.name, err, cases)
		}
		got, _ := base64.StdEncoding.DecodeString(cases[0].InputBase64)
		if string(got) != tt.expected || cases[0].ExpectedParse != "reject" || cases[0].StartLine != tt.start {
			t.Fatalf("wrong negative span: %+v", cases[0])
		}
	}
	for _, name := range []string{"empty_dockerfile", "only_comments"} {
		cases, exclusions, err := extractNegative(parserRoot+"/testfiles-negative/"+name+"/Dockerfile", []byte("# comment\n"))
		if err != nil || len(cases) != 0 || len(exclusions) == 0 {
			t.Fatalf("document exclusion: %v", err)
		}
	}
	for _, tt := range []struct{ path, input string }{
		{"env_no_value", "FROM busybox\n\nENV PATH=value\n"},
		{"env_no_value", "ENV PATH\n"},
		{"new_negative", "ENV PATH\n"},
		{"only_comments", "ENV PATH\n"},
	} {
		if _, _, err := extractNegative(parserRoot+"/testfiles-negative/"+tt.path+"/Dockerfile", []byte(tt.input)); err == nil {
			t.Fatalf("accepted stale/unclassified selector %s", tt.path)
		}
	}
}

func TestInventory(t *testing.T) {
	cases, exclusions, err := extractPositive("test", []byte("# heading\nFROM alpine\nUNKNOWN ignored\n\n"), nil)
	if err != nil || len(cases) != 1 || len(exclusions) != 3 {
		t.Fatalf("incomplete inventory: %v, %+v, %+v", err, cases, exclusions)
	}
	if _, err := inlineCoverage("parser_test.go", []byte("package parser\nfunc TestNew(t *testing.T) {}\n")); err == nil {
		t.Fatal("new inline test not rejected")
	}
	if err := auditNotices("Dockerfile", []byte("# Copyright Example\nFROM alpine\n")); err == nil {
		t.Fatal("unreviewed embedded notice not rejected")
	}
	if err := auditLicensePaths([]string{"LICENSE", parserRoot + "/NOTICE"}); err == nil {
		t.Fatal("new applicable notice not rejected")
	}
}

func TestDeterministicOutputAndCheck(t *testing.T) {
	parent := t.TempDir()
	dir := filepath.Join(parent, "generated")
	maintained := filepath.Join(parent, "known-deviations.json")
	if err := os.WriteFile(maintained, []byte("maintained\n"), 0644); err != nil {
		t.Fatal(err)
	}
	files := map[string][]byte{"z/source": []byte("exact\r\n"), "cases/a.json": []byte("{}\n")}
	first, err := outputManifest(metadata{1, "moby/buildkit", "1.27.0", strings.Repeat("d", 40)}, files, "v0.33.0", filepath.Join("..", ".."))
	if err != nil {
		t.Fatal(err)
	}

	second, _ := outputManifest(metadata{1, "moby/buildkit", "1.27.0", strings.Repeat("d", 40)}, map[string][]byte{"cases/a.json": []byte("{}\n"), "z/source": []byte("exact\r\n")}, "v0.33.0", filepath.Join("..", ".."))
	if !bytes.Equal(first, second) || bytes.Contains(first, []byte("\r")) {
		t.Fatal("manifest not deterministic LF")
	}
	files["manifest.json"] = first
	if err := synchronize(dir, files, false); err != nil {
		t.Fatal(err)
	}
	if err := synchronize(dir, files, true); err != nil {
		t.Fatal(err)
	}
	target := filepath.Join(dir, "cases", "a.json")
	if err := os.WriteFile(target, []byte("tampered\n"), 0644); err != nil {
		t.Fatal(err)
	}
	if err := synchronize(dir, files, true); err == nil {
		t.Fatal("check accepted tampering")
	}
	got, _ := os.ReadFile(target)
	if string(got) != "tampered\n" {
		t.Fatal("check modified output")
	}
	if err := synchronize(dir, files, false); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "extra"), nil, 0644); err != nil {
		t.Fatal(err)
	}
	if err := synchronize(dir, files, true); err == nil {
		t.Fatal("check ignored unexpected file")
	}
	if err := synchronize(dir, files, false); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(dir, "extra")); !os.IsNotExist(err) {
		t.Fatal("refresh did not remove stale generated file")
	}
	if data, err := os.ReadFile(maintained); err != nil || string(data) != "maintained\n" {
		t.Fatal("refresh changed maintained deviations outside generated")
	}
}

func TestVerifySource(t *testing.T) {
	dir := t.TempDir()
	runGit := func(args ...string) string {
		t.Helper()
		data, err := git(dir, args...)
		if err != nil {
			t.Fatal(err)
		}
		return strings.TrimSpace(string(data))
	}
	runGit("init", "--quiet")
	filename := filepath.Join(dir, "source")
	if err := os.WriteFile(filename, []byte("original\n"), 0644); err != nil {
		t.Fatal(err)
	}
	runGit("add", "source")
	runGit("-c", "user.name=Corpus test", "-c", "user.email=corpus@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "fixture")
	sha := runGit("rev-parse", "HEAD")
	runGit("-c", "user.name=Corpus test", "-c", "user.email=corpus@example.invalid", "-c", "tag.gpgsign=false", "tag", "-a", "dockerfile/1.27.0", "-m", "frontend")
	m := metadata{1, "moby/buildkit", "1.27.0", sha}
	if err := verifySource(dir, m); err != nil {
		t.Fatal(err)
	}
	wrong := m
	wrong.SourceCommit = strings.Repeat("0", 40)
	if err := verifySource(dir, wrong); err == nil {
		t.Fatal("accepted wrong source commit")
	}
	wrong = m
	wrong.FrontendVersion = "1.26.0"
	if err := verifySource(dir, wrong); err == nil {
		t.Fatal("accepted absent frontend tag")
	}
	if err := os.WriteFile(filename, []byte("modified\n"), 0644); err != nil {
		t.Fatal(err)
	}
	if err := verifySource(dir, m); err == nil {
		t.Fatal("accepted modified source")
	}
	if err := os.WriteFile(filename, []byte("original\n"), 0644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "untracked"), nil, 0644); err != nil {
		t.Fatal(err)
	}
	if err := verifySource(dir, m); err == nil {
		t.Fatal("accepted untracked source input")
	}
	if err := os.Remove(filepath.Join(dir, "untracked")); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filename, []byte("second\n"), 0644); err != nil {
		t.Fatal(err)
	}
	runGit("add", "source")
	runGit("-c", "user.name=Corpus test", "-c", "user.email=corpus@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "second")
	m.SourceCommit = runGit("rev-parse", "HEAD")
	if err := verifySource(dir, m); err == nil {
		t.Fatal("accepted frontend tag resolving to a different commit")
	}
}

func TestPinnedSourceIntegration(t *testing.T) {
	source := os.Getenv("BUILDKIT_CORPUS_SOURCE")
	if source == "" {
		t.Skip("set BUILDKIT_CORPUS_SOURCE to run full pinned-source integration")
	}
	data, err := os.ReadFile(filepath.Join("..", "..", "upstream-compatibility.json"))
	if err != nil {
		t.Fatal(err)
	}
	m, err := parseMetadata(data)
	if err != nil {
		t.Fatal(err)
	}
	if err := verifySource(source, m); err != nil {
		t.Fatal(err)
	}
	first, count, err := generate(filepath.Join("..", ".."), source, m, "integration-test")
	if err != nil {
		t.Fatal(err)
	}
	second, secondCount, err := generate(filepath.Join("..", ".."), source, m, "integration-test")
	if err != nil || count == 0 || secondCount != count || !reflect.DeepEqual(first, second) {
		t.Fatalf("nondeterministic generation: %v", err)
	}
	var manifest struct {
		Files []struct{ Path, SHA256 string }
	}
	if err := json.Unmarshal(first["manifest.json"], &manifest); err != nil {
		t.Fatal(err)
	}
	if len(manifest.Files) != len(first)-1 {
		t.Fatal("manifest not exhaustive")
	}
	previous := ""
	for _, entry := range manifest.Files {
		if entry.Path <= previous || digest(first[entry.Path]) != entry.SHA256 {
			t.Fatalf("manifest integrity/order: %s", entry.Path)
		}
		previous = entry.Path
	}
	for filename, data := range first {
		if !strings.HasPrefix(filename, "cases/") {
			continue
		}
		var c corpusCase
		if err := json.Unmarshal(data, &c); err != nil {
			t.Fatal(err)
		}
		input, err := base64.StdEncoding.DecodeString(c.InputBase64)
		if err != nil {
			t.Fatal(err)
		}
		slice, err := sourceSlice(first[sourceSnapshotPath(c.SourcePath)], c.StartLine, c.EndLine)
		if err != nil || !bytes.Equal(slice, input) || digest(input) != c.InputSHA256 {
			t.Fatalf("%s: invalid exact source slice", filename)
		}
	}
	dir := t.TempDir()
	if err := synchronize(dir, first, false); err != nil {
		t.Fatal(err)
	}
	if err := synchronize(dir, second, true); err != nil {
		t.Fatal(err)
	}
	t.Logf("Verified two byte-identical imports, %d cases, %d artifacts", count, len(first))
}
