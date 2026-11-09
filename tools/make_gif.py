"""Build the README demo GIF from the frames a capture spec wrote.

  1. Capture the frames in a visible Unity editor (batchmode has no window buffer):
       Unity -projectPath DemoProject -executeMethod Noskang.SkillGraph.Editor.SkillGraphCapture.Run
             -skillgraphCapture <absolute path>/DemoProject/Capture/readme_gif.json
     The frames go to DemoProject/Library/SkillGraphKit/readme-gif/ (ignored by git).
  2. python tools/make_gif.py DemoProject/Capture/readme_gif.json

The spec's "gif" block gives the output file (relative to the spec), the width in pixels and the colour count; each shot's
"ms" is how long that frame shows. Frames are scaled to the width, centred on the capture background if their shapes
differ, and quantized without dithering (flat editor colours stay flat). With the same Pillow version the same frames
give the same bytes (other versions quantize a little differently). Needs Pillow 9.1 or later; docs/images/demo.gif was
made with Pillow 13.0.
"""
import argparse
import json
import sys
from pathlib import Path

from PIL import Image

BACKGROUND = (0x1E, 0x1F, 0x22)  # the colour the capture command pads composed windows with


def read_spec(spec_path):
    spec_path = Path(spec_path).resolve()
    spec = json.loads(spec_path.read_text(encoding='utf-8'))
    gif = spec.get('gif') or {}
    base = spec_path.parent
    shots = [dict(name=s.get('name', Path(s['out']).stem), frame=(base / s['out']).resolve(), ms=int(s.get('ms', 2000)))
             for s in spec['shots']]
    return dict(out=(base / gif.get('out', 'demo.gif')).resolve(), width=int(gif.get('width', 960)),
                colors=int(gif.get('colors', 256)), shots=shots)


def fit(image, size):
    """Scale the image to fit inside size and centre it on the background colour."""
    image = image.convert('RGB')
    scale = min(size[0] / image.width, size[1] / image.height)
    scaled = image.resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.Resampling.LANCZOS)
    if scaled.size == size:
        return scaled
    canvas = Image.new('RGB', size, BACKGROUND)
    canvas.paste(scaled, ((size[0] - scaled.width) // 2, (size[1] - scaled.height) // 2))
    return canvas


def make(spec_path):
    spec = read_spec(spec_path)
    missing = [str(s['frame']) for s in spec['shots'] if not s['frame'].is_file()]
    if missing:
        raise SystemExit('missing frames (run the capture first):\n  ' + '\n  '.join(missing))
    first = Image.open(spec['shots'][0]['frame'])
    size = (spec['width'], round(spec['width'] * first.height / first.width))
    frames = [fit(Image.open(s['frame']), size).quantize(colors=spec['colors'], method=Image.Quantize.MEDIANCUT,
                                                          dither=Image.Dither.NONE) for s in spec['shots']]
    spec['out'].parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(spec['out'], save_all=True, append_images=frames[1:], duration=[s['ms'] for s in spec['shots']],
                   loop=0, disposal=1)
    return dict(out=str(spec['out']), size=list(size), frames=len(frames), seconds=sum(s['ms'] for s in spec['shots']) / 1000,
                bytes=spec['out'].stat().st_size, shots=[s['name'] for s in spec['shots']])


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('spec', help='capture spec with a "gif" block, e.g. DemoProject/Capture/readme_gif.json')
    a = p.parse_args(argv)
    print(json.dumps(make(a.spec), indent=1))
    return 0


if __name__ == '__main__':
    sys.exit(main())
