package main

import (
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"sort"
	"strings"
	"testing"
)

func TestGeneratePreservesPathsAcrossGitQuoteSettings(t *testing.T) {
	source := t.TempDir()
	runGit := func(args ...string) string {
		t.Helper()
		data, err := git(source, args...)
		if err != nil {
			t.Fatal(err)
		}
		return strings.TrimSpace(string(data))
	}
	runGit("init", "--quiet")
	runGit("config", "core.autocrlf", "false")

	const input = "FROM alpine\n"
	parsed, err := contextParse([]byte(input), '\\')
	if err != nil {
		t.Fatal(err)
	}
	golden := parsed.AST.Dump() + "\n"
	files := map[string]string{
		"LICENSE":                                "Apache License\nVersion 2.0\n",
		parserRoot + "/testfile-line/Dockerfile": input,
		parserRoot + "/testfiles-negative/empty_dockerfile/Dockerfile":   "",
		parserRoot + "/testfiles-negative/only_comments/Dockerfile":      "# comment\n",
		parserRoot + "/testfiles-negative/env_no_value/Dockerfile":       "FROM busybox\n\nENV PATH\n",
		parserRoot + "/testfiles-negative/shykes-nested-json/Dockerfile": "CMD [\"echo\", [\"nested json\"]]\n",
	}
	fixtureNames := []string{"ascii", "unicode-\u00e9", "space and \u00e9"}
	for _, name := range fixtureNames {
		files[parserRoot+"/testfiles/"+name+"/Dockerfile"] = input
		files[parserRoot+"/testfiles/"+name+"/result"] = golden
	}
	var functions []string
	for name := range inlineReasons {
		functions = append(functions, name)
	}
	sort.Strings(functions)
	for _, name := range functions {
		filename, function, _ := strings.Cut(name, "/")
		filename = parserRoot + "/" + filename
		if files[filename] == "" {
			files[filename] = "package parser\n"
		}
		files[filename] += "func " + function + "() {}\n"
	}
	for filename, data := range files {
		target := filepath.Join(source, filepath.FromSlash(filename))
		if err := os.MkdirAll(filepath.Dir(target), 0755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(target, []byte(data), 0644); err != nil {
			t.Fatal(err)
		}
	}
	runGit("add", ".")
	runGit("-c", "user.name=Corpus test", "-c", "user.email=corpus@example.invalid",
		"-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "fixtures")
	m := metadata{1, "moby/buildkit", "1.27.0", runGit("rev-parse", "HEAD")}
	var first map[string][]byte
	for _, quotePath := range []string{"true", "false"} {
		t.Run("quotePath="+quotePath, func(t *testing.T) {
			runGit("config", "core.quotePath", quotePath)
			generated, count, err := generate(filepath.Join("..", ".."), source, m, "test")
			if err != nil {
				t.Fatal(err)
			}
			if count != 6 {
				t.Fatalf("got %d cases, want 6", count)
			}
			var coverage struct {
				Files []fileCoverage `json:"files"`
			}
			if err := json.Unmarshal(generated["coverage.json"], &coverage); err != nil {
				t.Fatal(err)
			}
			covered := map[string]bool{}
			for _, file := range coverage.Files {
				covered[file.Path] = true
			}
			for _, name := range fixtureNames {
				dockerfile := parserRoot + "/testfiles/" + name + "/Dockerfile"
				result := parserRoot + "/testfiles/" + name + "/result"
				if !covered[dockerfile] || !covered[result] {
					t.Fatalf("fixture missing from coverage: %s", name)
				}
				var c corpusCase
				if err := json.Unmarshal(generated[caseFilePath(dockerfile+"#L1-L1")], &c); err != nil {
					t.Fatal(err)
				}
				if c.SourcePath != dockerfile || c.ID != dockerfile+"#L1-L1" {
					t.Fatalf("changed source path or case identity: %+v", c)
				}
				if string(generated[sourceSnapshotPath(dockerfile)]) != input ||
					string(generated[sourceSnapshotPath(result)]) != golden {
					t.Fatalf("source snapshot mismatch: %s", name)
				}
			}
			if first == nil {
				first = generated
			} else if !reflect.DeepEqual(first, generated) {
				t.Fatal("Git quotePath setting changed generated artifacts")
			}
		})
	}
}
