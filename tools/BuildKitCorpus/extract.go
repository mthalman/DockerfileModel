package main

import (
	"bytes"
	"crypto/sha256"
	"encoding/base64"
	"fmt"
	"reflect"
	"strings"
	"unicode/utf8"

	"github.com/moby/buildkit/frontend/dockerfile/parser"
)

type corpusCase struct {
	ID              string `json:"id"`
	InstructionType string `json:"instructionType"`
	InputBase64     string `json:"inputBase64"`
	EscapeCharacter string `json:"escapeCharacter"`
	ExpectedParse   string `json:"expectedParse"`
	SourcePath      string `json:"sourcePath"`
	StartLine       int    `json:"startLine"`
	EndLine         int    `json:"endLine"`
	InputSHA256     string `json:"inputSha256"`
}

type exclusion struct {
	StartLine int    `json:"startLine,omitempty"`
	EndLine   int    `json:"endLine,omitempty"`
	Reason    string `json:"reason"`
}

var supported = strings.Fields("ADD ARG CMD COPY ENTRYPOINT ENV EXPOSE FROM HEALTHCHECK LABEL MAINTAINER ONBUILD RUN SHELL STOPSIGNAL USER VOLUME WORKDIR")

func digest(data []byte) string {
	return fmt.Sprintf("%x", sha256.Sum256(data))
}

func sourceLines(data []byte) [][]byte {
	lines := bytes.SplitAfter(data, []byte("\n"))
	if len(lines[len(lines)-1]) == 0 {
		lines = lines[:len(lines)-1]
	}
	return lines
}

func sourceSlice(data []byte, start, end int) ([]byte, error) {
	lines := sourceLines(data)
	if start < 1 || end < start || end > len(lines) {
		return nil, fmt.Errorf("invalid inclusive line range %d-%d for %d lines", start, end, len(lines))
	}
	return bytes.Join(lines[start-1:end], nil), nil
}

func makeCase(path string, source []byte, start, end int, instruction, expectation string, escape rune) (corpusCase, error) {
	input, err := sourceSlice(source, start, end)
	if err != nil {
		return corpusCase{}, err
	}
	return corpusCase{
		ID: path + fmt.Sprintf("#L%d-L%d", start, end), InstructionType: strings.ToUpper(instruction),
		InputBase64: base64.StdEncoding.EncodeToString(input), EscapeCharacter: string(escape),
		ExpectedParse: expectation, SourcePath: path, StartLine: start, EndLine: end, InputSHA256: digest(input),
	}, nil
}

func contextParse(input []byte, escape rune) (*parser.Result, error) {
	bom := []byte{0xef, 0xbb, 0xbf}
	prefix := []byte(fmt.Sprintf("# escape=%c\n", escape))
	if bytes.HasPrefix(input, bom) {
		prefix = append(append([]byte{}, bom...), prefix...)
		input = input[len(bom):]
	}
	return parser.Parse(bytes.NewReader(append(prefix, input...)))
}

func comparableNode(node *parser.Node) *parser.Node {
	if node == nil {
		return nil
	}
	copy := *node
	copy.StartLine, copy.EndLine, copy.PrevComment = 0, 0, nil
	copy.Next = comparableNode(node.Next)
	copy.Children = nil
	for _, child := range node.Children {
		copy.Children = append(copy.Children, comparableNode(child))
	}
	return &copy
}

func uncovered(source []byte, covered map[int]bool, reason string) []exclusion {
	result := []exclusion{}
	for i := range sourceLines(source) {
		line := i + 1
		if covered[line] {
			continue
		}
		if len(result) != 0 && result[len(result)-1].EndLine == line-1 {
			result[len(result)-1].EndLine = line
		} else {
			result = append(result, exclusion{line, line, reason})
		}
	}
	return result
}

func extractPositive(path string, source, golden []byte) ([]corpusCase, []exclusion, error) {
	if !utf8.Valid(source) {
		return nil, nil, fmt.Errorf("%s: source is not strict UTF-8", path)
	}
	result, err := parser.Parse(bytes.NewReader(source))
	if err != nil {
		return nil, nil, fmt.Errorf("%s: upstream positive parse: %w", path, err)
	}
	if golden != nil && !bytes.Equal(golden, []byte(result.AST.Dump()+"\n")) {
		return nil, nil, fmt.Errorf("%s: upstream AST dump differs from golden", path)
	}
	cases, exclusions := []corpusCase{}, []exclusion{}
	covered := map[int]bool{}
	for _, node := range result.AST.Children {
		input, err := sourceSlice(source, node.StartLine, node.EndLine)
		if err != nil {
			return nil, nil, fmt.Errorf("%s: %w", path, err)
		}
		for i := node.StartLine; i <= node.EndLine; i++ {
			if covered[i] {
				return nil, nil, fmt.Errorf("%s: overlapping instruction spans at %d", path, i)
			}
			covered[i] = true
		}
		reparsed, err := contextParse(input, result.EscapeToken)
		if err != nil || len(reparsed.AST.Children) != 1 || !reflect.DeepEqual(comparableNode(node), comparableNode(reparsed.AST.Children[0])) {
			return nil, nil, fmt.Errorf("%s:%d-%d: slice does not preserve upstream AST (including attributes, flags and heredocs): %v", path, node.StartLine, node.EndLine, err)
		}
		instruction := strings.ToUpper(node.Value)
		found := false
		for _, name := range supported {
			found = found || name == instruction
		}
		if !found {
			exclusions = append(exclusions, exclusion{node.StartLine, node.EndLine, "Unsupported instruction " + instruction + "; not one of the harness's 18 instruction types (the low-level upstream parser accepts unknown commands)."})
			continue
		}
		c, err := makeCase(path, source, node.StartLine, node.EndLine, instruction, "accept", result.EscapeToken)
		if err != nil {
			return nil, nil, err
		}
		cases = append(cases, c)
	}
	exclusions = append(exclusions, uncovered(source, covered, "Document-only comments, directives, whitespace or empty continuation lines outside an instruction AST span.")...)
	return cases, exclusions, nil
}

func extractNegative(path string, source []byte) ([]corpusCase, []exclusion, error) {
	if !utf8.Valid(source) {
		return nil, nil, fmt.Errorf("%s: source is not strict UTF-8", path)
	}
	if _, err := parser.Parse(bytes.NewReader(source)); err == nil {
		return nil, nil, fmt.Errorf("%s: negative fixture unexpectedly accepted", path)
	}
	name := strings.TrimSuffix(strings.TrimPrefix(path, parserRoot+"/testfiles-negative/"), "/Dockerfile")
	var start, end int
	var instruction string
	switch name {
	case "empty_dockerfile", "only_comments":
		for _, line := range sourceLines(source) {
			text := strings.TrimSpace(string(line))
			if text != "" && !strings.HasPrefix(text, "#") {
				return nil, nil, fmt.Errorf("%s: document-only negative selector now contains an instruction", path)
			}
		}
		return []corpusCase{}, []exclusion{{Reason: "Empty/comment-only Dockerfile rejection is document-level, not an instruction parser expectation."}}, nil
	case "env_no_value":
		start, end, instruction = 3, 3, "ENV"
	case "shykes-nested-json":
		start, end, instruction = 1, 1, "CMD"
	default:
		return nil, nil, fmt.Errorf("%s: unreviewed negative fixture; add an explicit independently validated range", path)
	}
	input, err := sourceSlice(source, start, end)
	if err != nil {
		return nil, nil, err
	}
	fields := strings.Fields(string(input))
	if len(fields) == 0 || fields[0] != instruction {
		return nil, nil, fmt.Errorf("%s: stale negative instruction selector", path)
	}
	if _, err := contextParse(input, '\\'); err == nil {
		return nil, nil, fmt.Errorf("%s: selected negative instruction unexpectedly accepted independently", path)
	}
	remaining := bytes.Join(append(append([][]byte{}, sourceLines(source)[:start-1]...), sourceLines(source)[end:]...), nil)
	if len(bytes.TrimSpace(remaining)) != 0 {
		if _, err := parser.Parse(bytes.NewReader(remaining)); err != nil {
			return nil, nil, fmt.Errorf("%s: unclassified failure outside negative selector: %w", path, err)
		}
	}
	c, err := makeCase(path, source, start, end, instruction, "reject", '\\')
	return []corpusCase{c}, uncovered(source, map[int]bool{start: true}, "Outside the reviewed failing instruction in a negative document; not claimed as independent positive coverage."), err
}
