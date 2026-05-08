import numpy as np
from scipy.signal import firwin, iirnotch, lfilter

FS = 256
FIR_TAPS = 513
BUFFER_SAMPLES = 1024

b_notch, a_notch = iirnotch(60, 30, FS)
fir_kernel = firwin(FIR_TAPS, [1.0, 40.0], pass_zero=False, fs=FS, window='blackman')

# Generate a 10 Hz sine wave with amplitude 20
t = np.arange(BUFFER_SAMPLES) / FS
x = 20 * np.sin(2 * np.pi * 10 * t)

# DC offset (mean subtraction)
x = x - np.mean(x)

# Filter
notched = lfilter(b_notch, a_notch, x)
filt = lfilter(fir_kernel, 1.0, notched)

# Check ptp of the last 128 samples
chunk = filt[-128:]
print("PTP of last 128 samples:", np.ptp(chunk))

# Check max absolute value of the ENTIRE filtered signal
print("Max absolute value of entire signal:", np.max(np.abs(filt)))
print("Max absolute value of raw signal:", np.max(np.abs(x)))
