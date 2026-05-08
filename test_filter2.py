import numpy as np
from scipy.signal import firwin, iirnotch, lfilter

FS = 256
FIR_TAPS = 513
BUFFER_SAMPLES = 1024
stride = 25

b_notch, a_notch = iirnotch(60, 30, FS)
fir_kernel = firwin(FIR_TAPS, [1.0, 40.0], pass_zero=False, fs=FS, window='blackman')

# Generate a 10 Hz sine wave with amplitude 20
t = np.arange(BUFFER_SAMPLES) / FS
x = 20 * np.sin(2 * np.pi * 10 * t)

# DC offset (mean subtraction)
raw = x - np.mean(x)

# Filter
notched = lfilter(b_notch, a_notch, raw)
filt = lfilter(fir_kernel, 1.0, notched)

# Print the last 'stride' samples
print("Raw last 5:", raw[-5:])
print("Filt last 5:", filt[-5:])
print("Filt PTP:", np.ptp(filt[-128:]))
