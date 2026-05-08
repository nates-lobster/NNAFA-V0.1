# NeuroMemoryStudy: Session Visualization Script (R)
# This version uses the industry-standard 'mnexdf' or 'reticulate' fallback.

# --- 1. Install Dependencies ---
if (!require("ggplot2")) install.packages("ggplot2")
if (!require("dplyr")) install.packages("dplyr")
if (!require("tidyr")) install.packages("tidyr")
if (!require("reticulate")) install.packages("reticulate")

library(ggplot2)
library(dplyr)
library(tidyr)
library(reticulate)

# Use Python's pyxdf via R's reticulate (The most reliable way to read XDF)
if (!py_module_available("pyxdf")) {
  py_install("pyxdf")
}

pyxdf <- import("pyxdf")

# --- 2. Load Data ---
file_path <- "C:/Users/Nate/Documents/CurrentStudy/sub-P001/ses-S001/eeg/sub-P001_ses-S001_task-Default_run-001_eeg.xdf"

if (!file.exists(file_path)) {
  stop("File not found!")
}

# Read XDF file using Python backend
data_list <- pyxdf$load_xdf(file_path)
streams <- data_list[[1]]

# Find our Metrics stream
metrics_stream <- NULL
for (stream in streams) {
  if (stream$info$name == "NeuroMemory_Metrics") {
    metrics_stream <- stream
    break
  }
}

if (is.null(metrics_stream)) {
  stop("Could not find NeuroMemory_Metrics stream in the XDF file.")
}

# Convert to Data Frame
# pyxdf returns [samples, channels]
time_series <- metrics_stream$time_series
timestamps <- metrics_stream$time_stamps

df <- data.frame(
  Timestamp = timestamps - timestamps[1],
  Delta = time_series[,1],
  Theta = time_series[,2],
  Alpha = time_series[,3],
  Beta = time_series[,4],
  Gamma = time_series[,5],
  Ratio = time_series[,6]
)

# --- 3. Plot 1: Band Powers Over Time ---
plot_powers <- df %>%
  select(-Ratio) %>%
  pivot_longer(cols = -Timestamp, names_to = "Band", values_to = "Power") %>%
  ggplot(aes(x = Timestamp, y = Power, color = Band)) +
  geom_line(alpha = 0.8) +
  scale_x_continuous(breaks = seq(0, 3600, 10), minor_breaks = seq(0, 3600, 2)) +
  theme_minimal() +
  labs(title = "Brainwave Band Powers",
       subtitle = "Recorded via NeuroMemoryStudy",
       x = "Time (seconds)",
       y = "Power (uV^2/Hz)") +
  scale_color_brewer(palette = "Set1")

# --- 4. Plot 2: Alpha/Beta Ratio (The Feedback Metric) ---
plot_ratio <- ggplot(df, aes(x = Timestamp, y = Ratio)) +
  geom_line(color = "purple", size = 1) +
  geom_smooth(method = "gam", formula = y ~ s(x, bs = "cs"), color = "black", linetype = "dashed") +
  theme_minimal() +
  labs(title = "Alpha/Beta Ratio Trend",
       subtitle = "Dashed line shows smoothed trend (GAM)",
       x = "Time (seconds)",
       y = "Ratio")

# --- 5. Display Results ---
print(plot_powers)
print(plot_ratio)
