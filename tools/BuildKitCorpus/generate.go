package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"go/ast"
	goparser "go/parser"
	"go/token"
	"io/fs"
	"os"
	"path"
	"path/filepath"
	"regexp"
	"sort"
	"strings"
)

type fileCoverage struct {
	Path       string      `json:"path"`
	Kind       string      `json:"kind"`
	SHA256     string      `json:"sha256"`
	Cases      []string    `json:"caseIds"`
	Exclusions []exclusion `json:"exclusions"`
}

type functionCoverage struct {
	Path     string `json:"path"`
	Function string `json:"function"`
	Line     int    `json:"line"`
	Status   string `json:"status"`
	Reason   string `json:"reason"`
}

var inlineReasons = map[string]string{
	"directives_test.go/TestDirectives":                     "Excluded: document-level directive state, not the instruction-only adapter.",
	"directives_test.go/TestDetectSyntax":                   "Excluded: frontend-selection metadata helper API.",
	"directives_test.go/TestDetectSyntaxBOM":                "Excluded: frontend-selection metadata and BOM detection helper API.",
	"directives_test.go/TestParseDirective":                 "Excluded: document directive helper API.",
	"json_test.go/TestJSONArraysOfStrings":                  "Excluded: inline JSON helper tests outside the file-fixture adapter.",
	"line_parsers_test.go/TestParseNameValOldFormat":        "Excluded: inline name/value helper API tests.",
	"line_parsers_test.go/TestParseNameValNewFormat":        "Excluded: inline name/value helper API tests.",
	"line_parsers_test.go/TestParseNameValWithoutVal":       "Excluded: inline name/value helper API tests.",
	"parser_heredoc_test.go/TestParseExtractsHeredoc":       "Excluded: inline heredoc table outside the file-fixture adapter; no claim of importing these cases.",
	"parser_heredoc_test.go/TestParseJSONHeredoc":           "Excluded: inline heredoc table outside the file-fixture adapter.",
	"parser_heredoc_test.go/TestHeredocChomp":               "Excluded: heredoc chomp helper API.",
	"parser_heredoc_test.go/TestParseHeredocHelpers":        "Excluded: heredoc helper API.",
	"parser_heredoc_test.go/TestHeredocsFromLine":           "Excluded: heredoc extraction helper API.",
	"parser_test.go/TestParseErrorCases":                    "Covered by negative file validation; document-only exclusions and reviewed instruction ranges are inventoried separately.",
	"parser_test.go/TestParseCases":                         "Covered by positive file validation and exact AST.Dump plus newline golden comparison.",
	"parser_test.go/TestParseWords":                         "Excluded: inline word-splitting helper API.",
	"parser_test.go/TestParseIncludesLineNumbers":           "Partially covered: testfile-line instructions imported; root document line metadata assertions are outside the instruction adapter.",
	"parser_test.go/TestParseWarnsOnEmptyContinutationLine": "Excluded: inline document warning assertions outside the file-fixture adapter.",
	"parser_test.go/TestParseReturnsScannerErrors":          "Excluded: injected reader/scanner failures, not instruction syntax.",
}

func inlineCoverage(filename string, data []byte) ([]functionCoverage, error) {
	set := token.NewFileSet()
	file, err := goparser.ParseFile(set, filename, data, 0)
	if err != nil {
		return nil, err
	}
	result := []functionCoverage{}
	for _, declaration := range file.Decls {
		fn, ok := declaration.(*ast.FuncDecl)
		if !ok || fn.Recv != nil || !strings.HasPrefix(fn.Name.Name, "Test") {
			continue
		}
		key := path.Base(filename) + "/" + fn.Name.Name
		reason, ok := inlineReasons[key]
		if !ok {
			return nil, fmt.Errorf("%s: unclassified inline test function", key)
		}
		status := "excluded"
		if strings.HasPrefix(reason, "Covered") {
			status = "file-fixtures-covered"
		} else if strings.HasPrefix(reason, "Partially") {
			status = "partially-covered"
		}
		result = append(result, functionCoverage{filename, fn.Name.Name, set.Position(fn.Pos()).Line, status, reason})
	}
	return result, nil
}

func auditNotices(filename string, data []byte) error {
	if regexp.MustCompile(`(?i)\b(copyright|licen[cs]e|notice|permission)\b`).Match(data) {
		return fmt.Errorf("%s: embedded license/notice marker requires explicit review before import", filename)
	}
	return nil
}

func auditLicensePaths(paths []string) error {
	for _, filename := range paths {
		base := strings.ToUpper(path.Base(filename))
		dir := path.Dir(filename)
		applicable := dir == "." || dir == "frontend" || dir == "frontend/dockerfile" || strings.HasPrefix(filename, parserRoot+"/")
		if applicable && (strings.HasPrefix(base, "LICENSE") || strings.HasPrefix(base, "NOTICE") || strings.HasPrefix(base, "COPYING")) && filename != "LICENSE" {
			return fmt.Errorf("%s: new applicable license/notice file requires explicit review", filename)
		}
	}
	return nil
}

func jsonBytes(value any) ([]byte, error) {
	data, err := json.MarshalIndent(value, "", "  ")
	if err != nil {
		return nil, err
	}
	return append(data, '\n'), nil
}

func sourceSnapshotPath(sourcePath string) string {
	return "sources/" + digest([]byte(sourcePath))[:16] + ".source"
}

func caseFilePath(id string) string {
	return "cases/" + digest([]byte(id))[:16] + ".json"
}

func claimPath(identities map[string]string, filename, identity string) error {
	if existing, ok := identities[filename]; ok && existing != identity {
		return fmt.Errorf("generated hash-name collision at %s: %q and %q", filename, existing, identity)
	}
	identities[filename] = identity
	return nil
}

func outputManifest(m metadata, files map[string][]byte, moduleVersion, repoRoot string) ([]byte, error) {
	goMod, err := os.ReadFile(filepath.Join(repoRoot, "tools", "BuildKitCorpus", "go.mod"))
	if err != nil {
		return nil, err
	}
	goSum, err := os.ReadFile(filepath.Join(repoRoot, "tools", "BuildKitCorpus", "go.sum"))
	if err != nil {
		return nil, err
	}
	type entry struct {
		Path   string `json:"path"`
		SHA256 string `json:"sha256"`
	}
	entries := []entry{}
	for filename, data := range files {
		if filename != "manifest.json" {
			entries = append(entries, entry{filename, digest(data)})
		}
	}
	sort.Slice(entries, func(i, j int) bool { return entries[i].Path < entries[j].Path })
	return jsonBytes(struct {
		metadata
		ImporterGoModSHA256 string  `json:"importerGoModSha256"`
		ImporterGoSumSHA256 string  `json:"importerGoSumSha256"`
		Files               []entry `json:"files"`
		Importer            struct {
			SchemaVersion         int    `json:"schemaVersion"`
			Path                  string `json:"path"`
			BuildKitModuleVersion string `json:"buildkitModuleVersion"`
		} `json:"importer"`
	}{
		metadata: m, Files: entries,
		ImporterGoModSHA256: digest(goMod),
		ImporterGoSumSHA256: digest(goSum),
		Importer: struct {
			SchemaVersion         int    `json:"schemaVersion"`
			Path                  string `json:"path"`
			BuildKitModuleVersion string `json:"buildkitModuleVersion"`
		}{1, "tools/BuildKitCorpus", moduleVersion},
	})
}

func generate(repoRoot, source string, m metadata, moduleVersion string) (map[string][]byte, int, error) {
	tree, err := git(source, "ls-tree", "-r", "-z", "--name-only", m.SourceCommit)
	if err != nil {
		return nil, 0, err
	}
	paths := strings.Split(strings.TrimSuffix(string(tree), "\x00"), "\x00")
	sort.Strings(paths)
	if err := auditLicensePaths(paths); err != nil {
		return nil, 0, err
	}
	files := map[string][]byte{}
	files[".gitattributes"] = []byte("* -text\n")
	identities := map[string]string{}
	read := func(filename string) ([]byte, error) {
		snapshotPath := sourceSnapshotPath(filename)
		if err := claimPath(identities, snapshotPath, filename); err != nil {
			return nil, err
		}
		data, err := git(source, "show", m.SourceCommit+":"+filename)
		if err == nil {
			files[snapshotPath] = data
		}
		return data, err
	}
	license, err := read("LICENSE")
	if err != nil {
		return nil, 0, err
	}
	if !bytes.Contains(license, []byte("Apache License")) || !bytes.Contains(license, []byte("Version 2.0")) {
		return nil, 0, fmt.Errorf("upstream license changed; requires explicit review")
	}
	files["LICENSE"] = license
	coverage := struct {
		SchemaVersion int                `json:"schemaVersion"`
		Scope         string             `json:"scope"`
		Files         []fileCoverage     `json:"files"`
		InlineTests   []functionCoverage `json:"inlineTests"`
		LicenseAudit  string             `json:"licenseAudit"`
	}{
		SchemaVersion: 1,
		Scope:         "All parser/testfiles positive fixtures (validated against goldens), parser/testfiles-negative fixtures, and parser/testfile-line. Only the harness's 18 instruction types are executable cases. Inline tests are inventoried, not converted. No Docker build, shell execution, downstream semantic validation, or complete syntax coverage is claimed.",
		Files:         []fileCoverage{}, InlineTests: []functionCoverage{},
		LicenseAudit: "Preserved root Apache-2.0 LICENSE. No applicable ancestor/parser-subtree license override or NOTICE file. Scanned all imported fixture and golden bytes for copyright/license/notice/permission markers; none found. Source snapshots are unmodified; instruction JSON is an extracted representation.",
	}
	seenFunctions, seenNegatives, ids := map[string]bool{}, map[string]bool{}, map[string]bool{}
	goldens := map[string]bool{}
	for _, filename := range paths {
		if strings.HasPrefix(filename, parserRoot+"/testfiles/") && strings.HasSuffix(filename, "/result") {
			goldens[filename] = false
		}
	}
	positiveCount, lineCount, count := 0, 0, 0
	for _, filename := range paths {
		isFixture := strings.HasPrefix(filename, parserRoot+"/testfiles/") || strings.HasPrefix(filename, parserRoot+"/testfiles-negative/") || strings.HasPrefix(filename, parserRoot+"/testfile-line/")
		isTest := strings.HasPrefix(filename, parserRoot+"/") && strings.HasSuffix(filename, "_test.go")
		if !isFixture && !isTest {
			continue
		}
		data, err := read(filename)
		if err != nil {
			return nil, 0, err
		}
		if isTest {
			functions, err := inlineCoverage(filename, data)
			if err != nil {
				return nil, 0, err
			}
			for _, fn := range functions {
				seenFunctions[path.Base(fn.Path)+"/"+fn.Function] = true
			}
			coverage.InlineTests = append(coverage.InlineTests, functions...)
			coverage.Files = append(coverage.Files, fileCoverage{filename, "inline-test-source", digest(data), []string{}, []exclusion{{Reason: "Functions inventoried individually under inlineTests; inline inputs are not converted."}}})
			continue
		}
		if err := auditNotices(filename, data); err != nil {
			return nil, 0, err
		}
		if _, ok := goldens[filename]; ok {
			coverage.Files = append(coverage.Files, fileCoverage{filename, "positive-ast-golden", digest(data), []string{}, []exclusion{{Reason: "Validation artifact: compared exactly to AST.Dump() plus newline; not an instruction input."}}})
			continue
		}
		if path.Base(filename) != "Dockerfile" {
			return nil, 0, fmt.Errorf("%s: unclassified fixture file", filename)
		}
		var cases []corpusCase
		var exclusions []exclusion
		kind := "positive"
		switch {
		case strings.HasPrefix(filename, parserRoot+"/testfiles/"):
			goldenPath := path.Join(path.Dir(filename), "result")
			if _, ok := goldens[goldenPath]; !ok {
				return nil, 0, fmt.Errorf("%s: missing golden", filename)
			}
			golden, err := read(goldenPath)
			if err != nil {
				return nil, 0, err
			}
			goldens[goldenPath] = true
			cases, exclusions, err = extractPositive(filename, data, golden)
			if err != nil {
				return nil, 0, err
			}
			positiveCount++
		case strings.HasPrefix(filename, parserRoot+"/testfiles-negative/"):
			kind = "negative"
			seenNegatives[path.Base(path.Dir(filename))] = true
			cases, exclusions, err = extractNegative(filename, data)
			if err != nil {
				return nil, 0, err
			}
		case filename == parserRoot+"/testfile-line/Dockerfile":
			kind = "line-metadata"
			cases, exclusions, err = extractPositive(filename, data, nil)
			if err != nil {
				return nil, 0, err
			}
			lineCount++
		default:
			return nil, 0, fmt.Errorf("%s: unclassified fixture", filename)
		}
		item := fileCoverage{filename, kind, digest(data), []string{}, exclusions}
		for _, c := range cases {
			if ids[c.ID] {
				return nil, 0, fmt.Errorf("duplicate case ID %s", c.ID)
			}
			ids[c.ID] = true
			item.Cases = append(item.Cases, c.ID)
			data, err := jsonBytes(c)
			if err != nil {
				return nil, 0, err
			}
			casePath := caseFilePath(c.ID)
			if err := claimPath(identities, casePath, c.ID); err != nil {
				return nil, 0, err
			}
			if _, exists := files[casePath]; exists {
				return nil, 0, fmt.Errorf("duplicate case filename %s", casePath)
			}
			files[casePath] = data
			count++
		}
		coverage.Files = append(coverage.Files, item)
	}
	for name := range inlineReasons {
		if !seenFunctions[name] {
			return nil, 0, fmt.Errorf("stale inline function classification %s", name)
		}
	}
	for _, name := range []string{"empty_dockerfile", "only_comments", "env_no_value", "shykes-nested-json"} {
		if !seenNegatives[name] {
			return nil, 0, fmt.Errorf("stale negative selector %s", name)
		}
	}
	for filename, used := range goldens {
		if !used {
			return nil, 0, fmt.Errorf("orphan golden %s", filename)
		}
	}
	if positiveCount == 0 || lineCount != 1 || count == 0 {
		return nil, 0, fmt.Errorf("missing required upstream fixture inventory")
	}
	files["coverage.json"], err = jsonBytes(coverage)
	if err != nil {
		return nil, 0, err
	}
	files["ATTRIBUTION.md"] = []byte(fmt.Sprintf("# BuildKit parser corpus attribution\n\nSource: https://github.com/%s/tree/%s/frontend/dockerfile/parser\n\nDockerfile frontend release: dockerfile/%s\n\nBuildKit source commit: %s\n\nUpstream material is distributed under the preserved Apache License 2.0 in LICENSE.\nNo applicable upstream NOTICE or parser-subtree license override was found at this pin.\nThe fixture and golden audit found no embedded copyright or license notices.\nExact original bytes, including comments, remain under sources/ using the first\n16 lowercase SHA256 hex characters of the UTF-8 upstream path, plus .source.\nFull original paths remain in case provenance and the coverage inventory.\nThe sources/ tree also preserves upstream Go test files used for the coverage inventory.\n\nModification notice: cases/*.json are generated, extracted instruction representations\nof the upstream Dockerfiles, not upstream AST goldens. coverage.json and manifest.json\nare generated provenance/coverage metadata. No upstream Dockerfile commands are executed.\nSee tools/BuildKitCorpus/README.md for scope, exclusions, and regeneration.\n", m.Repository, m.SourceCommit, m.FrontendVersion, m.SourceCommit))
	files["manifest.json"], err = outputManifest(m, files, moduleVersion, repoRoot)
	return files, count, err
}

func synchronize(root string, files map[string][]byte, check bool) error {
	paths := make([]string, 0, len(files))
	for filename := range files {
		if !fs.ValidPath(filename) || strings.Contains(filename, "\\") {
			return fmt.Errorf("unsafe generated path %q", filename)
		}
		paths = append(paths, filename)
	}
	sort.Strings(paths)
	existing := map[string]bool{}
	directories := []string{}
	err := filepath.WalkDir(root, func(filename string, entry fs.DirEntry, err error) error {
		if err != nil {
			if os.IsNotExist(err) && filename == root {
				return nil
			}
			return err
		}
		if entry.Type()&os.ModeSymlink != 0 {
			return fmt.Errorf("generated tree contains symlink: %s", filename)
		}
		if entry.IsDir() {
			if filename != root {
				directories = append(directories, filename)
			}
			return nil
		}
		relative, err := filepath.Rel(root, filename)
		if err != nil {
			return err
		}
		existing[filepath.ToSlash(relative)] = true
		return nil
	})
	if err != nil {
		return err
	}
	differences := []string{}
	for _, filename := range paths {
		target := filepath.Join(root, filepath.FromSlash(filename))
		data, err := os.ReadFile(target)
		if err != nil && !os.IsNotExist(err) {
			return err
		}
		if err == nil && bytes.Equal(data, files[filename]) {
			continue
		}
		differences = append(differences, "changed or missing: "+filename)
		if !check {
			if err := os.MkdirAll(filepath.Dir(target), 0755); err != nil {
				return err
			}
			if err := os.WriteFile(target, files[filename], 0644); err != nil {
				return err
			}
		}
	}
	for filename := range existing {
		if _, ok := files[filename]; !ok {
			differences = append(differences, "unexpected: "+filename)
			if !check {
				if err := os.Remove(filepath.Join(root, filepath.FromSlash(filename))); err != nil {
					return err
				}
			}
		}
	}
	if check && len(differences) != 0 {
		sort.Strings(differences)
		return fmt.Errorf("generated corpus drift:\n%s", strings.Join(differences, "\n"))
	}
	if !check {
		sort.Slice(directories, func(i, j int) bool { return len(directories[i]) > len(directories[j]) })
		for _, directory := range directories {
			entries, err := os.ReadDir(directory)
			if err != nil {
				return err
			}
			if len(entries) == 0 {
				if err := os.Remove(directory); err != nil {
					return err
				}
			}
		}
	}
	return nil
}
