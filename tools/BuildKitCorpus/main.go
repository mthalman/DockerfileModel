package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime/debug"
	"strings"
)

const parserRoot = "frontend/dockerfile/parser"
const modulePath = "github.com/moby/buildkit"
const outputPath = "src/Valleysoft.DockerfileModel.DiffTest/UpstreamCorpus/generated"

type metadata struct {
	SchemaVersion   int    `json:"schemaVersion"`
	Repository      string `json:"repository"`
	FrontendVersion string `json:"frontendVersion"`
	SourceCommit    string `json:"sourceCommit"`
}

func parseMetadata(data []byte) (metadata, error) {
	var m metadata
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&m); err != nil {
		return m, err
	}
	if err := decoder.Decode(new(any)); err != io.EOF {
		return m, errors.New("metadata must contain exactly one JSON object")
	}
	if m.SchemaVersion != 1 || m.Repository != "moby/buildkit" ||
		!regexp.MustCompile(`^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$`).MatchString(m.FrontendVersion) ||
		!regexp.MustCompile(`^[0-9a-f]{40}$`).MatchString(m.SourceCommit) {
		return m, errors.New("invalid compatibility metadata: require schema 1, moby/buildkit, stable frontend version, full lowercase source SHA")
	}
	return m, nil
}

func command(dir, name string, args ...string) ([]byte, error) {
	cmd := exec.Command(name, args...)
	cmd.Dir = dir
	var stderr bytes.Buffer
	cmd.Stderr = &stderr
	data, err := cmd.Output()
	if err != nil {
		return nil, fmt.Errorf("%s %s: %w: %s", name, strings.Join(args, " "), err, stderr.String())
	}
	return data, nil
}

func git(source string, args ...string) ([]byte, error) {
	return command(source, "git", append([]string{"--no-pager"}, args...)...)
}

func verifySource(source string, m metadata) error {
	for _, ref := range []string{"HEAD", "refs/tags/dockerfile/" + m.FrontendVersion + "^{commit}"} {
		data, err := git(source, "rev-parse", "--verify", ref)
		if err != nil {
			return err
		}
		if strings.TrimSpace(string(data)) != m.SourceCommit {
			return fmt.Errorf("%s does not resolve to pinned source %s", ref, m.SourceCommit)
		}
	}
	data, err := git(source, "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=none")
	if err != nil {
		return err
	}
	if len(data) != 0 {
		return fmt.Errorf("source checkout is not clean:\n%s", data)
	}
	return nil
}

func verifyModule(repoRoot string, m metadata) (string, error) {
	dir := filepath.Join(repoRoot, "tools", "BuildKitCorpus")
	list, err := command(dir, "go", "list", "-mod=readonly", "-m", "-json", modulePath)
	if err != nil {
		return "", err
	}
	var selected struct {
		Path, Version string
		Replace       *json.RawMessage
	}
	if err := json.Unmarshal(list, &selected); err != nil {
		return "", err
	}
	if selected.Path != modulePath || selected.Version == "" || selected.Replace != nil {
		return "", errors.New("BuildKit module must be version-pinned without a replacement")
	}
	info, ok := debug.ReadBuildInfo()
	if !ok {
		return "", errors.New("importer lacks Go build provenance")
	}
	found := false
	for _, dep := range info.Deps {
		if dep.Path == modulePath {
			found = dep.Version == selected.Version && dep.Replace == nil
		}
	}
	if !found {
		return "", errors.New("running importer BuildKit dependency differs from effective go.mod")
	}
	data, err := command(dir, "go", "list", "-mod=readonly", "-m", "-json", modulePath+"@"+selected.Version)
	if err != nil {
		return "", err
	}
	var downloaded struct {
		Origin struct{ VCS, URL, Hash string }
	}
	if err := json.Unmarshal(data, &downloaded); err != nil {
		return "", err
	}
	if downloaded.Origin.VCS != "git" || downloaded.Origin.URL != "https://github.com/moby/buildkit" || downloaded.Origin.Hash != m.SourceCommit {
		return "", fmt.Errorf("effective BuildKit module %s source %s differs from pin %s", selected.Version, downloaded.Origin.Hash, m.SourceCommit)
	}
	if _, err := command(dir, "go", "mod", "verify"); err != nil {
		return "", err
	}
	return selected.Version, nil
}

func run(repoRoot, source string, check bool) error {
	repoRoot, err := filepath.Abs(repoRoot)
	if err != nil {
		return err
	}
	data, err := os.ReadFile(filepath.Join(repoRoot, "upstream-compatibility.json"))
	if err != nil {
		return err
	}
	m, err := parseMetadata(data)
	if err != nil {
		return err
	}
	version, err := verifyModule(repoRoot, m)
	if err != nil {
		return err
	}
	if source == "" {
		temp, err := os.MkdirTemp("", "buildkit-corpus-source-")
		if err != nil {
			return err
		}
		defer os.RemoveAll(temp)
		source = filepath.Join(temp, "buildkit")
		if _, err := command("", "git", "clone", "--quiet", "--filter=blob:none", "--no-checkout",
			"--depth=1", "--branch=dockerfile/"+m.FrontendVersion, "https://github.com/moby/buildkit.git", source); err != nil {
			return err
		}
		if _, err := git(source, "checkout", "--quiet", "--detach", m.SourceCommit); err != nil {
			return err
		}
	}
	source, err = filepath.Abs(source)
	if err != nil {
		return err
	}
	if err := verifySource(source, m); err != nil {
		return err
	}
	files, count, err := generate(repoRoot, source, m, version)
	if err != nil {
		return err
	}
	if err := synchronize(filepath.Join(repoRoot, filepath.FromSlash(outputPath)), files, check); err != nil {
		return err
	}
	fmt.Printf("BuildKit frontend %s (%s): %d cases, %d generated artifacts; check=%t\n", m.FrontendVersion, m.SourceCommit, count, len(files), check)
	return nil
}

func main() {
	repoRoot := flag.String("repo-root", "", "DockerfileModel repository root (required)")
	source := flag.String("source", "", "clean BuildKit checkout at the pin, including the frontend release tag")
	check := flag.Bool("check", false, "regenerate in memory and compare without modifying generated files")
	flag.Parse()
	if *repoRoot == "" || flag.NArg() != 0 {
		fmt.Fprintln(os.Stderr, "usage: BuildKitCorpus --repo-root <root> [--source <checkout>] [--check]")
		os.Exit(2)
	}
	if err := run(*repoRoot, *source, *check); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
