"""Development-only Kokoro WAV exporter. Requires a licensed local Kokoro install."""
import argparse
import array
import wave


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--text", required=True)
    parser.add_argument("--voice", required=True)
    parser.add_argument("--speed", required=True, type=float)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    from kokoro import KPipeline

    pipeline = KPipeline(lang_code="a")
    pieces = []
    for _, _, audio in pipeline(args.text, voice=args.voice, speed=args.speed):
        pieces.append(audio.detach().cpu().reshape(-1).tolist())
    if not pieces:
        raise RuntimeError("Kokoro returned no audio")

    samples = array.array("h")
    for piece in pieces:
        samples.extend(max(-32768, min(32767, round(float(value) * 32767))) for value in piece)
    with wave.open(args.output, "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(24000)
        output.writeframes(samples.tobytes())


if __name__ == "__main__":
    main()
