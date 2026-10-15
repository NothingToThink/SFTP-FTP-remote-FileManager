#!/usr/bin/env python3
"""SVG -> Avalonia XAML конвертер для свинки.

Запуск из корня репозитория:  python3 -I tools/svg2xaml.py

Поддержано: path, rect (rx/ry), circle, ellipse, line, polyline, polygon, g
(transform: translate/scale/rotate/matrix, opacity, наследование fill/stroke/...).
Все фигуры превращаются в path-данные (SVG-эллипсы/прямоугольники дают точное
совпадение штриха, в отличие от Avalonia Ellipse/Rectangle, которые вписывают штрих внутрь).

Выход:
  Client/Assets/Pig/PigIcons.axaml         - ResourceDictionary: Icon.<Name>.Stroke / .Fill / .Thin
  Client/Controls/PigIconKind.cs           - enum PigIconKind
  Client/Controls/Pig/Pig<Scene>Scene.axaml(+.cs) - пять сцен 320x240
"""
import math
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Client" / "Assets" / "Pig"
OUT_ICONS = ROOT / "Client" / "Assets" / "Pig" / "PigIcons.axaml"
OUT_KIND = ROOT / "Client" / "Controls" / "PigIconKind.cs"
OUT_SCENES = ROOT / "Client" / "Controls" / "Pig"
SVG = "{http://www.w3.org/2000/svg}"


def num(v):
    s = f"{v:.3f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def f(el, name, default=0.0):
    v = el.get(name)
    if v is None:
        return default
    return float(re.sub(r"[a-z%]+$", "", v.strip()))


def tag(el):
    return el.tag.replace(SVG, "")


# ---------------------------------------------------------------- geometry
def ellipse_d(cx, cy, rx, ry):
    return (f"M{num(cx - rx)},{num(cy)} A{num(rx)},{num(ry)} 0 1 0 {num(cx + rx)},{num(cy)} "
            f"A{num(rx)},{num(ry)} 0 1 0 {num(cx - rx)},{num(cy)} Z")


def rect_d(x, y, w, h, rx, ry):
    if rx <= 0 and ry <= 0:
        return f"M{num(x)},{num(y)} H{num(x + w)} V{num(y + h)} H{num(x)} Z"
    if rx <= 0:
        rx = ry
    if ry <= 0:
        ry = rx
    rx, ry = min(rx, w / 2), min(ry, h / 2)
    a = f"A{num(rx)},{num(ry)} 0 0 1"
    return (f"M{num(x + rx)},{num(y)} H{num(x + w - rx)} {a} {num(x + w)},{num(y + ry)} "
            f"V{num(y + h - ry)} {a} {num(x + w - rx)},{num(y + h)} "
            f"H{num(x + rx)} {a} {num(x)},{num(y + h - ry)} "
            f"V{num(y + ry)} {a} {num(x + rx)},{num(y)} Z")


def points_d(pts, close):
    nums = [float(n) for n in re.findall(r"-?\d*\.?\d+(?:e-?\d+)?", pts)]
    pairs = list(zip(nums[0::2], nums[1::2]))
    d = "M" + " L".join(f"{num(x)},{num(y)}" for x, y in pairs)
    return d + (" Z" if close else "")


def shape_d(el):
    t = tag(el)
    if t == "path":
        return re.sub(r"\s+", " ", el.get("d").strip())
    if t == "rect":
        return rect_d(f(el, "x"), f(el, "y"), f(el, "width"), f(el, "height"), f(el, "rx"), f(el, "ry"))
    if t == "circle":
        return ellipse_d(f(el, "cx"), f(el, "cy"), f(el, "r"), f(el, "r"))
    if t == "ellipse":
        return ellipse_d(f(el, "cx"), f(el, "cy"), f(el, "rx"), f(el, "ry"))
    if t == "line":
        return f"M{num(f(el, 'x1'))},{num(f(el, 'y1'))} L{num(f(el, 'x2'))},{num(f(el, 'y2'))}"
    if t == "polyline":
        return points_d(el.get("points"), False)
    if t == "polygon":
        return points_d(el.get("points"), True)
    return None


SHAPES = {"path", "rect", "circle", "ellipse", "line", "polyline", "polygon"}


def transform_str(tr):
    """SVG transform -> Avalonia RenderTransform string."""
    if not tr:
        return None
    out = []
    for name, args in re.findall(r"(\w+)\s*\(([^)]*)\)", tr):
        a = [float(x) for x in re.split(r"[\s,]+", args.strip()) if x]
        if name == "translate":
            out.append(f"translate({num(a[0])}px, {num(a[1] if len(a) > 1 else 0)}px)")
        elif name == "scale":
            out.append(f"scale({num(a[0])}, {num(a[1] if len(a) > 1 else a[0])})")
        elif name == "rotate":
            if len(a) == 3:
                out.append(f"translate({num(a[1])}px, {num(a[2])}px) rotate({num(a[0])}deg) "
                           f"translate({num(-a[1])}px, {num(-a[2])}px)")
            else:
                out.append(f"rotate({num(a[0])}deg)")
        elif name == "matrix":
            out.append("matrix(" + ", ".join(num(x) for x in a) + ")")
        else:
            raise ValueError(f"transform {name} не поддержан")
    return " ".join(out)


# ---------------------------------------------------------------- icons
def pascal(name):
    return "".join(p.capitalize() for p in name.split("-"))


def convert_icons():
    files = sorted((SRC / "icons").glob("*.svg"))
    entries, names = [], []
    skipped = []
    for fp in files:
        root = ET.parse(fp).getroot()
        if root.get("viewBox") != "0 0 24 24":
            skipped.append(fp.name)
            continue
        name = pascal(fp.stem)
        names.append(name)
        stroke, fill, thin = [], [], []

        def walk(el, inherit):
            style = dict(inherit)
            for k in ("fill", "stroke", "stroke-width"):
                if el.get(k) is not None:
                    style[k] = el.get(k)
            for ch in el:
                if tag(ch) == "g":
                    walk(ch, style)
                elif tag(ch) in SHAPES:
                    s = dict(style)
                    for k in ("fill", "stroke", "stroke-width"):
                        if ch.get(k) is not None:
                            s[k] = ch.get(k)
                    d = shape_d(ch)
                    if s.get("fill") not in (None, "none"):
                        fill.append(d)
                    if s.get("stroke") not in (None, "none"):
                        (thin if float(s.get("stroke-width", "2")) != 2 else stroke).append(d)

        walk(root, {"fill": "none", "stroke": "currentColor", "stroke-width": "2"})
        entries.append((name, stroke, fill, thin, fp.name))

    lines = ['<ResourceDictionary xmlns="https://github.com/avaloniaui"',
             '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
             "    <!-- Сгенерировано tools/svg2xaml.py из Assets/Pig/icons/*.svg. Не править руками. -->",
             "    <!-- Сетка 24x24. .Stroke: штрих 2, round caps/joins. .Fill: заливка. .Thin: штрих 1.5. -->"]
    for name, stroke, fill, thin, src in entries:
        lines.append(f"    <!-- {src} -->")
        for kind, parts in (("Stroke", stroke), ("Fill", fill), ("Thin", thin)):
            if parts:
                lines.append(f'    <StreamGeometry x:Key="Icon.{name}.{kind}">{" ".join(parts)}</StreamGeometry>')
    lines.append("</ResourceDictionary>")
    OUT_ICONS.write_text("\n".join(lines) + "\n", encoding="utf-8")

    kind = ["// Сгенерировано tools/svg2xaml.py. Не править руками.",
            "namespace FileManagerClient.Controls;", "",
            "/// <summary>Имена иконок свинки; ключ ресурса: Icon.{Kind}.Stroke / .Fill / .Thin.</summary>",
            "public enum PigIconKind", "{", "    None,"]
    kind += [f"    {n}," for n in names]
    kind.append("}")
    OUT_KIND.parent.mkdir(parents=True, exist_ok=True)
    OUT_KIND.write_text("\n".join(kind) + "\n", encoding="utf-8")
    print(f"icons: {len(names)} -> {OUT_ICONS.name}, пропущено: {skipped}")


# ---------------------------------------------------------------- scenes
COLORS = {
    "#3A1420": "{DynamicResource Pig.Ink}",
    "#F7A8B8": "{DynamicResource Pig.Body}",
    "#F08AA0": "{DynamicResource Pig.BodyShade}",
    "#7A2338": "{DynamicResource Pig.Slot}",
    "#C8925A": "{DynamicResource Pig.Cork}",
    "#C2410C": "{DynamicResource Brush.Error}",
    "#F6C453": "#F6C453",
    "#FFF": "White",
    "#FFFFFF": "White",
}
# Контекстные подмены (в тёмных SVG хвост розовый, "zzz" - BodyShade): свои токены.
CTX_COLORS = {("tail", "#3A1420"): "{DynamicResource Pig.Line}",
              ("zzz", "#7A2338"): "{DynamicResource Pig.Detail}"}

PART_NAMES = {"shadow": "Shadow", "legs": "Legs", "tail": "Tail", "body": "Body", "cork": "Cork",
              "ear": "Ears", "snout": "Snout", "mouth": "Mouth", "eye": "Eyes", "zzz": "Zzz",
              "motion": "Motion", "alarm": "Alarm", "bang": "Bang", "coin": "Coin", "sparkle": "Sparkle",
              "slot-local": "SlotLocal", "slot-ftp": "SlotFtp", "slot-sftp": "SlotSftp"}
# Точка, вокруг которой анимируется часть (координаты сцены 320x240).
ORIGINS = {"Shadow": (150, 222), "Legs": (150, 218), "Body": (150, 200), "Cork": (150, 199),
           "Ears": (190, 75), "Snout": (232, 134), "Mouth": (228, 167), "Eyes": (205, 108),
           "Zzz": (245, 60), "Motion": (40, 100), "Alarm": (262, 120), "Bang": (290, 36),
           "Coin": (139, 34), "Sparkle": (139, 20), "Pig": (150, 218)}
# Что остаётся ВНЕ группы Pig (не прыгает/не трясётся вместе со свиньёй).
OUTSIDE_PIG = {"shadow", "motion", "zzz", "alarm", "bang", "coin", "sparkle"}
# Параметры PigTail по сценам: (Direction°, Length, Curl). Хвост цепляется в (x,y).
TAILS = {"empty": (180, 92, 1.0), "connecting": (180, 92, 0.6), "error": (106, 46, 0.0),
         "squeal": (261, 48, 0.1), "success": (180, 92, 1.0)}


class SceneGen:
    def __init__(self, scene):
        self.scene = scene
        self.lines = []

    def color(self, v, part):
        if v is None or v == "none":
            return None
        key = v.upper()
        return CTX_COLORS.get((part, key)) or COLORS.get(key) or v

    def emit(self, ind, s):
        self.lines.append("    " * ind + s)

    def leaf(self, el, style, ind, part):
        d = shape_d(el)
        attrs = [f'Data="{d}"']
        fill = self.color(style.get("fill"), part)
        stroke = self.color(style.get("stroke"), part)
        attrs.append(f'Fill="{fill}"') if fill else None
        if stroke:
            attrs.append(f'Stroke="{stroke}"')
            attrs.append(f'StrokeThickness="{num(float(style.get("stroke-width", "1")))}"')
            if style.get("stroke-linecap") in ("round", "square"):
                attrs.append(f'StrokeLineCap="{style["stroke-linecap"].capitalize()}"')
            if style.get("stroke-linejoin") == "round":
                attrs.append('StrokeJoin="Round"')
        op = style.get("opacity") or el.get("opacity")
        if op:
            attrs.append(f'Opacity="{op}"')
        self.emit(ind, f'<Path {" ".join(attrs)}/>')

    INH = ("fill", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin")

    def node(self, el, inherit, ind, part):
        """Рекурсивно: shape -> Path, g -> Canvas."""
        style = dict(inherit)
        for k in self.INH:
            if el.get(k) is not None:
                style[k] = el.get(k)
        t = tag(el)
        if t in SHAPES:
            if el.get("opacity"):
                style["opacity"] = el.get("opacity")
            self.leaf(el, style, ind, part)
        elif t == "g":
            attrs = []
            tr = transform_str(el.get("transform"))
            if tr:
                attrs.append(f'RenderTransform="{tr}"')
            if el.get("opacity"):
                attrs.append(f'Opacity="{el.get("opacity")}"')
            nm = PART_NAMES.get(el.get("id"))
            self.emit(ind, f'<Canvas Width="320" Height="240"{(" " + " ".join(attrs)) if attrs else ""}>')
            for ch in el:
                self.node(ch, style, ind + 1, el.get("id") or part)
            self.emit(ind, "</Canvas>")

    def part_open(self, name, ind, extra=""):
        ox, oy = ORIGINS.get(name, (0, 0))
        self.emit(ind, f'<Canvas x:Name="{name}" Width="320" Height="240" RenderTransformOrigin="{ox},{oy}"{extra}>')

    def tail(self, el, ind):
        a, length, curl = TAILS[self.scene]
        d = el.get("d")
        mx, my = re.match(r"M\s*([\d.]+)[ ,]([\d.]+)", d).groups()
        self.emit(ind, f'<ctl:PigTail x:Name="Tail" Canvas.Left="{mx}" Canvas.Top="{my}" Width="1" Height="1" '
                       f'Direction="{a}" Length="{length}" Curl="{curl}" '
                       f'Stroke="{{DynamicResource Pig.Line}}" StrokeThickness="6"/>')

    def top(self, el, ind):
        """Часть верхнего уровня: Canvas-обёртка с именем."""
        id_ = el.get("id")
        name = PART_NAMES.get(id_)
        if tag(el) == "g" and id_ is None:  # безымянная обёртка с transform (success) - это Pig
            return False
        if id_ == "tail":
            self.tail(el, ind)
            return True
        self.part_open(name, ind)
        self.node(el, {}, ind + 1, id_)
        self.emit(ind, "</Canvas>")
        return True

    def run(self, root):
        children = list(root)
        inner_pig, outer = [], []
        wrapper = None
        for ch in children:
            if tag(ch) == "g" and ch.get("id") is None:
                wrapper = ch
            elif ch.get("id") in OUTSIDE_PIG:
                outer.append(ch)
            else:
                inner_pig.append(ch)
        if wrapper is not None:
            inner_pig = [c for c in wrapper]
            wrapper_tr = transform_str(wrapper.get("transform"))
        else:
            wrapper_tr = None

        self.emit(1, '<Canvas x:Name="Root" Width="320" Height="240" RenderTransformOrigin="150,218">')
        for ch in children:
            if ch in outer and ch.get("id") == "shadow":
                self.top(ch, 2)
        pig_attrs = f' RenderTransform="{wrapper_tr}"' if wrapper_tr else ""
        ox, oy = ORIGINS["Pig"]
        self.emit(2, f'<Canvas x:Name="Pig" Width="320" Height="240" RenderTransformOrigin="{ox},{oy}"{pig_attrs}>')
        # порядок отрисовки сохраняем как в SVG: сначала motion (под телом), потом остальное
        for ch in children:
            if ch in outer and ch.get("id") == "motion":
                self.top(ch, 3)
        for ch in inner_pig:
            self.top(ch, 3)
        self.emit(2, "</Canvas>")
        for ch in children:
            if ch in outer and ch.get("id") not in ("shadow", "motion"):
                self.top(ch, 2)
        self.emit(1, "</Canvas>")
        return "\n".join(self.lines)


def convert_scenes():
    OUT_SCENES.mkdir(parents=True, exist_ok=True)
    for scene in ("empty", "connecting", "error", "squeal", "success"):
        light = ET.parse(SRC / "illustrations" / f"pig-{scene}.svg").getroot()
        dark = ET.parse(SRC / "illustrations" / f"pig-{scene}-dark.svg").getroot()
        a = [(tag(e), e.get("id"), e.get("d")) for e in light.iter()]
        b = [(tag(e), e.get("id"), e.get("d")) for e in dark.iter()]
        if a != b:
            print(f"ВНИМАНИЕ: {scene}-dark отличается структурно!")
        cls = f"Pig{scene.capitalize()}Scene"
        body = SceneGen(scene).run(light)
        label = light.get("{http://www.w3.org/2000/svg}label") or light.get("aria-label") or ""
        xaml = (f'<UserControl xmlns="https://github.com/avaloniaui"\n'
                f'             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\n'
                f'             xmlns:ctl="using:FileManagerClient.Controls"\n'
                f'             x:Class="FileManagerClient.Controls.Pig.{cls}"\n'
                f'             IsHitTestVisible="False">\n'
                f'    <!-- Сгенерировано tools/svg2xaml.py из pig-{scene}.svg ({label}). Не править руками. -->\n'
                f'    <Viewbox Stretch="Uniform">\n'
                f'        <Canvas Width="320" Height="240">\n'
                + "\n".join("    " + ln if ln else ln for ln in body.split("\n")) +
                f'\n        </Canvas>\n    </Viewbox>\n</UserControl>\n')
        (OUT_SCENES / f"{cls}.axaml").write_text(xaml, encoding="utf-8")
        (OUT_SCENES / f"{cls}.axaml.cs").write_text(
            "// Сгенерировано tools/svg2xaml.py\n"
            "using Avalonia.Controls;\n\n"
            "namespace FileManagerClient.Controls.Pig;\n\n"
            f"/// <summary>Сцена «{label}» 320x240; части: Root, Pig, Shadow, Legs, Tail, Body, Cork, Ears, Snout, Mouth, Eyes ...</summary>\n"
            f"public partial class {cls} : UserControl\n{{\n"
            f"    public {cls}() => InitializeComponent();\n}}\n", encoding="utf-8")
        print("scene:", cls)


if __name__ == "__main__":
    convert_icons()
    convert_scenes()
