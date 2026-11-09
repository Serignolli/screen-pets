package main

// JSON com a ordem das chaves preservada, no mesmo formato de escrita do programa do Windows (src/Json.cs).

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"math"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

// Obj é um objeto JSON que lembra a ordem das chaves.
type Obj struct {
	keys []string
	m    map[string]any
}

func NewObj() *Obj { return &Obj{m: map[string]any{}} }

func (o *Obj) Get(k string) (any, bool) {
	if o == nil {
		return nil, false
	}
	v, ok := o.m[k]
	return v, ok
}

func (o *Obj) Set(k string, v any) {
	if _, ok := o.m[k]; !ok {
		o.keys = append(o.keys, k)
	}
	o.m[k] = v
}

func (o *Obj) Delete(k string) {
	if _, ok := o.m[k]; !ok {
		return
	}
	delete(o.m, k)
	for i, x := range o.keys {
		if x == k {
			o.keys = append(o.keys[:i], o.keys[i+1:]...)
			break
		}
	}
}

func (o *Obj) Keys() []string {
	if o == nil {
		return nil
	}
	return append([]string(nil), o.keys...)
}

func (o *Obj) Len() int {
	if o == nil {
		return 0
	}
	return len(o.keys)
}

// ParseJSON lê qualquer valor JSON: *Obj, []any, string, float64, bool ou nil.
func ParseJSON(data []byte) (any, error) {
	dec := json.NewDecoder(bytes.NewReader(data))
	dec.UseNumber()
	v, err := readValue(dec)
	if err != nil {
		return nil, err
	}
	if _, err := dec.Token(); err != io.EOF {
		return nil, errors.New("JSON com conteúdo depois do fim")
	}
	return v, nil
}

func readValue(dec *json.Decoder) (any, error) {
	t, err := dec.Token()
	if err != nil {
		return nil, err
	}
	switch x := t.(type) {
	case json.Delim:
		switch x {
		case '{':
			o := NewObj()
			for dec.More() {
				kt, err := dec.Token()
				if err != nil {
					return nil, err
				}
				k, _ := kt.(string)
				v, err := readValue(dec)
				if err != nil {
					return nil, err
				}
				o.Set(k, v)
			}
			_, err := dec.Token()
			return o, err
		case '[':
			list := []any{}
			for dec.More() {
				v, err := readValue(dec)
				if err != nil {
					return nil, err
				}
				list = append(list, v)
			}
			_, err := dec.Token()
			return list, err
		}
		return nil, fmt.Errorf("JSON inesperado: %v", x)
	case json.Number:
		return x, nil
	default:
		return x, nil
	}
}

// LoadObj lê um arquivo com um objeto JSON; arquivo inexistente ou vazio vira um objeto vazio.
func LoadObj(path string) (*Obj, error) {
	data, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return NewObj(), nil
	}
	if err != nil {
		return nil, err
	}
	data = bytes.TrimPrefix(data, []byte("\xef\xbb\xbf"))
	if len(bytes.TrimSpace(data)) == 0 {
		return NewObj(), nil
	}
	v, err := ParseJSON(data)
	if err != nil {
		return nil, fmt.Errorf("%s: %w", path, err)
	}
	o, ok := v.(*Obj)
	if !ok {
		return nil, fmt.Errorf("%s não contém um objeto JSON", path)
	}
	return o, nil
}

// SaveJSON grava ao lado e troca, para quem lê nunca pegar o arquivo pela metade.
func SaveJSON(path string, v any) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, []byte(Stringify(v)+"\n"), 0o644); err != nil {
		return err
	}
	return os.Rename(tmp, path)
}

func Stringify(v any) string {
	var sb strings.Builder
	writeJSON(&sb, v, 0)
	return sb.String()
}

func writeJSON(sb *strings.Builder, v any, ind int) {
	pad := func(n int) { sb.WriteString(strings.Repeat("  ", n)) }
	switch x := v.(type) {
	case nil:
		sb.WriteString("null")
	case string:
		writeString(sb, x)
	case bool:
		if x {
			sb.WriteString("true")
		} else {
			sb.WriteString("false")
		}
	case json.Number:
		sb.WriteString(x.String())
	case int:
		sb.WriteString(strconv.Itoa(x))
	case int64:
		sb.WriteString(strconv.FormatInt(x, 10))
	case float64:
		if x == math.Trunc(x) && math.Abs(x) < 1e15 {
			sb.WriteString(strconv.FormatInt(int64(x), 10))
		} else {
			sb.WriteString(strconv.FormatFloat(x, 'g', -1, 64))
		}
	case *Obj:
		if x.Len() == 0 {
			sb.WriteString("{}")
			return
		}
		sb.WriteString("{\n")
		for i, k := range x.keys {
			pad(ind + 1)
			writeString(sb, k)
			sb.WriteString(": ")
			writeJSON(sb, x.m[k], ind+1)
			if i < len(x.keys)-1 {
				sb.WriteString(",")
			}
			sb.WriteString("\n")
		}
		pad(ind)
		sb.WriteString("}")
	case []any:
		if len(x) == 0 {
			sb.WriteString("[]")
			return
		}
		sb.WriteString("[\n")
		for i, e := range x {
			pad(ind + 1)
			writeJSON(sb, e, ind+1)
			if i < len(x)-1 {
				sb.WriteString(",")
			}
			sb.WriteString("\n")
		}
		pad(ind)
		sb.WriteString("]")
	case []string:
		list := make([]any, len(x))
		for i, s := range x {
			list[i] = s
		}
		writeJSON(sb, list, ind)
	default:
		writeString(sb, fmt.Sprint(x))
	}
}

func writeString(sb *strings.Builder, s string) {
	sb.WriteByte('"')
	for _, c := range s {
		switch c {
		case '"':
			sb.WriteString(`\"`)
		case '\\':
			sb.WriteString(`\\`)
		case '\n':
			sb.WriteString(`\n`)
		case '\r':
			sb.WriteString(`\r`)
		case '\t':
			sb.WriteString(`\t`)
		case '\b':
			sb.WriteString(`\b`)
		case '\f':
			sb.WriteString(`\f`)
		default:
			if c < 0x20 {
				fmt.Fprintf(sb, `\u%04x`, c)
			} else {
				sb.WriteRune(c)
			}
		}
	}
	sb.WriteByte('"')
}

// ---------- leitura com valor padrão ----------

func jStr(o *Obj, k, def string) string {
	v, ok := o.Get(k)
	if !ok || v == nil {
		return def
	}
	switch x := v.(type) {
	case string:
		return x
	case json.Number:
		return x.String()
	case bool:
		return strconv.FormatBool(x)
	}
	return def
}

func jNum(o *Obj, k string, def float64) float64 {
	v, ok := o.Get(k)
	if !ok {
		return def
	}
	if f, ok := toFloat(v); ok {
		return f
	}
	return def
}

func toFloat(v any) (float64, bool) {
	switch x := v.(type) {
	case json.Number:
		f, err := x.Float64()
		return f, err == nil
	case float64:
		return x, true
	case int:
		return float64(x), true
	case string:
		f, err := strconv.ParseFloat(x, 64)
		return f, err == nil
	}
	return 0, false
}

func toF32(v any) float32 { f, _ := toFloat(v); return float32(f) }

func jBool(o *Obj, k string, def bool) bool {
	v, ok := o.Get(k)
	if b, isBool := v.(bool); ok && isBool {
		return b
	}
	return def
}

func jObj(o *Obj, k string) *Obj {
	v, _ := o.Get(k)
	x, _ := v.(*Obj)
	return x
}

func jArr(o *Obj, k string) []any {
	v, _ := o.Get(k)
	x, _ := v.([]any)
	return x
}

func jStrs(o *Obj, k string) ([]string, bool) {
	v, ok := o.Get(k)
	list, isList := v.([]any)
	if !ok || !isList {
		return nil, false
	}
	out := make([]string, 0, len(list))
	for _, e := range list {
		if s, ok := e.(string); ok {
			out = append(out, s)
		}
	}
	return out, true
}

func strsToAny(list []string) []any {
	out := make([]any, len(list))
	for i, s := range list {
		out[i] = s
	}
	return out
}
