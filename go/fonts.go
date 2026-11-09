package main

import (
	"bytes"
	"image/color"
	"os"
	"path/filepath"

	"github.com/hajimehoshi/ebiten/v2"
	"github.com/hajimehoshi/ebiten/v2/text/v2"
)

// A fonte Pixelify Sans (recursos/PixelifySans.ttf), para os balõezinhos e os "au!" e "zzz".
var pixelSource *text.GoTextFaceSource

func pixelFace(size float64) text.Face {
	if pixelSource == nil {
		data, err := os.ReadFile(filepath.Join(resourcesDir(), "PixelifySans.ttf"))
		if err == nil {
			pixelSource, err = text.NewGoTextFaceSource(bytes.NewReader(data))
		}
		if err != nil {
			logf("fonte: %v", err)
			return nil
		}
	}
	return &text.GoTextFace{Source: pixelSource, Size: size}
}

func measure(s string, face text.Face) (float64, float64) {
	if face == nil {
		return float64(len(s)) * 6, 10
	}
	return text.Measure(s, face, face.Metrics().HAscent+face.Metrics().HDescent)
}

func drawText(dst *ebiten.Image, s string, face text.Face, x, y float64, c color.Color) {
	if face == nil {
		return
	}
	op := &text.DrawOptions{}
	op.GeoM.Translate(x, y)
	op.ColorScale.ScaleWithColor(c)
	text.Draw(dst, s, face, op)
}
