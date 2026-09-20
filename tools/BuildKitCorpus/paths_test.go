package main

import (
	"os"
	"path/filepath"
	"regexp"
	"testing"
)

func TestShortArtifactPaths(t *testing.T) {
	for _, identity := range []string{"abc", "frontend/dockerfile/parser/testfiles/example/Dockerfile", "unicode-\u00e9"} {
		expected := digest([]byte(identity))[:16]
		if sourceSnapshotPath(identity) != "sources/"+expected+".source" || caseFilePath(identity) != "cases/"+expected+".json" {
			t.Fatalf("incorrect UTF-8 identity hash for %q", identity)
		}
		if !regexp.MustCompile(`^sources/[0-9a-f]{16}\.source$`).MatchString(sourceSnapshotPath(identity)) {
			t.Fatal("source snapshot name not bounded")
		}
	}
	if sourceSnapshotPath("abc") != "sources/ba7816bf8f01cfea.source" {
		t.Fatal("known SHA256 vector mismatch")
	}
	claims := map[string]string{}
	if err := claimPath(claims, "sources/collision.source", "original/path"); err != nil {
		t.Fatal(err)
	}
	if err := claimPath(claims, "sources/collision.source", "original/path"); err != nil {
		t.Fatal("repeated reads of the same source must be allowed")
	}
	if err := claimPath(claims, "sources/collision.source", "different/path"); err == nil {
		t.Fatal("source hash collision accepted")
	}
	if err := claimPath(claims, "cases/collision.json", "first-id"); err != nil {
		t.Fatal(err)
	}
	if err := claimPath(claims, "cases/collision.json", "second-id"); err == nil {
		t.Fatal("case hash collision accepted")
	}
}

func TestRefreshRemovesObsoleteSourceTree(t *testing.T) {
	root := t.TempDir()
	old := filepath.Join(root, "sources", "old", "nested", "Dockerfile")
	if err := os.MkdirAll(filepath.Dir(old), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(old, []byte("original\n"), 0644); err != nil {
		t.Fatal(err)
	}
	files := map[string][]byte{sourceSnapshotPath("old/nested/Dockerfile"): []byte("original\n")}
	if err := synchronize(root, files, false); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(root, "sources", "old")); !os.IsNotExist(err) {
		t.Fatal("obsolete nested source directories remain")
	}
	if err := synchronize(root, files, true); err != nil {
		t.Fatal(err)
	}
}
