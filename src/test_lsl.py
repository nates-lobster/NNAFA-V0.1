from pylsl import resolve_byprop, resolve_streams
import time

print("Scanning for LSL streams...")

print("Testing resolve_streams...")
try:
    streams = resolve_streams(wait_time=2.0)
    if not streams:
        print("No streams found at all.")
    else:
        for stream in streams:
            print(f"- Found Stream: {stream.name()} | Type: {stream.type()} | Host: {stream.hostname()}")
except Exception as e:
    print(f"Error finding streams: {e}")
