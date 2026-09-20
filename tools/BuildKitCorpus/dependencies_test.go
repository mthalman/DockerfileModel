package main

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestManifestDependencyHashes(t *testing.T) {
	root := t.TempDir()
	moduleDir := filepath.Join(root, "tools", "BuildKitCorpus")
	if err := os.MkdirAll(moduleDir, 0755); err != nil {
		t.Fatal(err)
	}
	m := metadata{1, "moby/buildkit", "1.27.0", strings.Repeat("d", 40)}
	files := map[string][]byte{"coverage.json": []byte("{}\n")}
	if _, err := outputManifest(m, files, "v0.33.0", root); err == nil {
		t.Fatal("accepted missing go.mod")
	}
	goMod := []byte("module example.test/importer\r\n")
	if err := os.WriteFile(filepath.Join(moduleDir, "go.mod"), goMod, 0644); err != nil {
		t.Fatal(err)
	}
	if _, err := outputManifest(m, files, "v0.33.0", root); err == nil {
		t.Fatal("accepted missing go.sum")
	}
	goSum := []byte("dependency checksum\n")
	if err := os.WriteFile(filepath.Join(moduleDir, "go.sum"), goSum, 0644); err != nil {
		t.Fatal(err)
	}
	first, err := outputManifest(m, files, "v0.33.0", root)
	if err != nil {
		t.Fatal(err)
	}
	var manifest struct {
		ImporterGoModSHA256 string                  `json:"importerGoModSha256"`
		ImporterGoSumSHA256 string                  `json:"importerGoSumSha256"`
		Files               []struct{ Path string } `json:"files"`
	}
	if err := json.Unmarshal(first, &manifest); err != nil {
		t.Fatal(err)
	}
	if manifest.ImporterGoModSHA256 != digest(goMod) || manifest.ImporterGoSumSHA256 != digest(goSum) {
		t.Fatalf("dependency hashes do not represent exact bytes: %+v", manifest)
	}
	if len(manifest.Files) != 1 || manifest.Files[0].Path != "coverage.json" {
		t.Fatal("external dependency files leaked into generated inventory")
	}
	for _, filename := range []string{"go.mod", "go.sum"} {
		if err := os.WriteFile(filepath.Join(moduleDir, filename), []byte("changed\n"), 0644); err != nil {
			t.Fatal(err)
		}
		next, err := outputManifest(m, files, "v0.33.0", root)
		if err != nil || bytes.Equal(first, next) {
			t.Fatalf("%s change not reflected in manifest: %v", filename, err)
		}
		first = next
	}
}
