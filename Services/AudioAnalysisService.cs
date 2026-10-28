using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace DMXScenarioDesigner.Services
{
    public class TimelineSection
    {
        public double StartTimeMs { get; set; }
        public string Name { get; set; }
        public Brush Color { get; set; }
    }

    public class AudioAnalysisResult
    {
        public double EstimatedBpm { get; set; }
        public double FirstBeatMs { get; set; }
        public List<TimelineSection> Sections { get; set; }
    }

    public class AudioAnalysisService
    {
        private static double _currentBpm = 120.0;
        public static double CurrentBpm
        {
            get { return _currentBpm; }
            set { _currentBpm = value; }
        }

        private static float Hann(int n, int size)
        {
            return 0.5f * (1f - (float)Math.Cos(2 * Math.PI * n / (size - 1)));
        }

        public static List<float> GenerateWaveformData(string filePath)
        {
            List<float> waveformData = new List<float>();

            using (var audioFile = new AudioFileReader(filePath))
            {
                List<float> samples = new List<float>();

                float[] buffer = new float[1024];

                int read;

                while ((read = audioFile.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        samples.Add(Math.Abs(buffer[i]));
                    }
                }

                int targetPoints = 5000;

                int blockSize = Math.Max(1, samples.Count / targetPoints);

                for (int i = 0; i < samples.Count; i += blockSize)
                {
                    float max = 0;

                    int end = Math.Min(i + blockSize, samples.Count);

                    for (int j = i; j < end; j++)
                    {
                        if (samples[j] > max)
                            max = samples[j];
                    }

                    waveformData.Add(max);
                }
            }

            return waveformData;
        }

        public static AudioAnalysisResult AnalyzeAudio(
            string filePath,
            double totalDurationMs,
            List<float> waveformData)
        {
            var result = new AudioAnalysisResult();

            DetectBeats(
                filePath,
                out double estimatedBpm,
                out double firstBeatMs);

            result.EstimatedBpm = estimatedBpm;
            result.FirstBeatMs = firstBeatMs;

            result.Sections = GenerateSections(
                filePath,
                totalDurationMs);

            return result;
        }

        // =====================================================
        // BEAT DETECTION
        // =====================================================

        private static void DetectBeats(
            string filePath,
            out double estimatedBpm,
            out double firstBeatMs)
        {
            estimatedBpm = 120.0;
            firstBeatMs = 0;

            List<float> samples = new List<float>();

            int sampleRate;

            using (var reader = new AudioFileReader(filePath))
            {
                sampleRate = reader.WaveFormat.SampleRate;

                float[] buffer = new float[4096];

                int read;

                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    int channels = reader.WaveFormat.Channels;

                    for (int i = 0; i < read - channels; i += channels)
                    {
                        float mono = 0;

                        for (int ch = 0; ch < channels; ch++)
                        {
                            mono += buffer[i + ch];
                        }

                        samples.Add(mono / channels);
                    }
                }
            }

            if (samples.Count == 0)
                return;

            int fftSize = 2048;

            int hopSize = 256;

            List<float> spectralFlux = new List<float>();

            float[] previousMagnitudes = new float[fftSize / 2];

            for (int pos = 0; pos + fftSize < samples.Count; pos += hopSize)
            {
                NAudio.Dsp.Complex[] fftBuffer =
                    new NAudio.Dsp.Complex[fftSize];

                for (int i = 0; i < fftSize; i++)
                {
                    fftBuffer[i].X =
                        samples[pos + i] * Hann(i, fftSize);

                    fftBuffer[i].Y = 0;
                }

                NAudio.Dsp.FastFourierTransform.FFT(
                    true,
                    (int)Math.Log(fftSize, 2.0),
                    fftBuffer);

                float[] subbandFlux = new float[4];

                int[] bandLimits =
                {
                    (int)(200.0 * fftSize / sampleRate),
                    (int)(800.0 * fftSize / sampleRate),
                    (int)(3200.0 * fftSize / sampleRate)
                };

                int minBin =
                    (int)(40.0 * fftSize / sampleRate);

                int maxBin =
                    (int)(4000.0 * fftSize / sampleRate);

                for (int i = minBin; i < maxBin; i++)
                {
                    float magnitude =
                        (float)Math.Sqrt(
                            fftBuffer[i].X * fftBuffer[i].X +
                            fftBuffer[i].Y * fftBuffer[i].Y);

                    magnitude =
                        (float)Math.Log10(1.0f + magnitude * 10);

                    float diff =
                        magnitude - previousMagnitudes[i];

                    if (diff > 0)
                    {
                        if (i <= bandLimits[0])
                            subbandFlux[0] += diff;
                        else if (i <= bandLimits[1])
                            subbandFlux[1] += diff;
                        else if (i <= bandLimits[2])
                            subbandFlux[2] += diff;
                        else
                            subbandFlux[3] += diff;
                    }

                    previousMagnitudes[i] = magnitude;
                }

                float flux =
                      subbandFlux[0] * 2.2f
                    + subbandFlux[1] * 1.3f
                    + subbandFlux[2] * 0.45f
                    + subbandFlux[3] * 0.15f;

                spectralFlux.Add(flux);
            }

            if (spectralFlux.Count == 0)
                return;

            float[] thresholdedFlux =
                new float[spectralFlux.Count];

            int window = 15;

            for (int i = 0; i < spectralFlux.Count; i++)
            {
                float sum = 0;

                int count = 0;

                for (int j = Math.Max(0, i - window);
                         j <= Math.Min(spectralFlux.Count - 1, i + window);
                         j++)
                {
                    sum += spectralFlux[j];
                    count++;
                }

                float localAvg = sum / count;

                float val =
                    spectralFlux[i] - (localAvg * 1.3f);

                thresholdedFlux[i] =
                    val > 0 ? val : 0;
            }

            double fluxSampleRate =
                (double)sampleRate / hopSize;

            float[] onsetPeaks =
                new float[thresholdedFlux.Length];

            int suppression =
                (int)(0.10 * fluxSampleRate);

            int lastAcceptedPeak = -suppression;

            for (int i = 1; i < thresholdedFlux.Length - 1; i++)
            {
                bool isPeak =
                    thresholdedFlux[i] > thresholdedFlux[i - 1] &&
                    thresholdedFlux[i] >= thresholdedFlux[i + 1];

                if (!isPeak)
                    continue;

                int localWindow = 8;

                float localMean = 0;

                int count = 0;

                for (int j = Math.Max(0, i - localWindow);
                         j <= Math.Min(thresholdedFlux.Length - 1, i + localWindow);
                         j++)
                {
                    localMean += thresholdedFlux[j];
                    count++;
                }

                localMean /= count;

                if (thresholdedFlux[i] > localMean * 1.7f)
                {
                    if (i - lastAcceptedPeak >= suppression)
                    {
                        onsetPeaks[i] = thresholdedFlux[i];
                        lastAcceptedPeak = i;
                    }
                }
            }

            int maxAcfLag =
                (int)(fluxSampleRate * 60.0 / 40.0);

            double[] acf =
                new double[maxAcfLag + 1];

            for (int lag = 1; lag <= maxAcfLag; lag++)
            {
                double sum = 0;

                int count = onsetPeaks.Length - lag;

                if (count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        sum += onsetPeaks[i] * onsetPeaks[i + lag];
                    }

                    acf[lag] = sum / count;
                }
            }

            int minBpm = 60;
            int maxBpm = 180;

            int minLag =
                (int)(fluxSampleRate * 60.0 / maxBpm);

            int maxLag =
                (int)(fluxSampleRate * 60.0 / minBpm);

            double bestBpm = 120.0;
            double maxScore = -1;

            int bestLagSpan =
                (int)(fluxSampleRate * 60.0 / 120.0);

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double bpm =
                    60.0 * fluxSampleRate / lag;

                double score = acf[lag];

                if (lag * 2 <= maxAcfLag)
                    score += 0.35 * acf[lag * 2];

                if (lag * 4 <= maxAcfLag)
                    score += 0.15 * acf[lag * 4];

                double preferredTempo = 120.0;

                double logTempo =
                    Math.Log(bpm / preferredTempo) / Math.Log(2.0);

                double weight =
                    Math.Exp(-0.5 * Math.Pow(logTempo / 1.4, 2));

                score *= weight;

                if (score > maxScore)
                {
                    maxScore = score;
                    bestBpm = bpm;
                    bestLagSpan = lag;
                }
            }

            estimatedBpm = Math.Round(bestBpm);

            while (estimatedBpm > 135)
            {
                estimatedBpm /= 2.0;
            }

            while (estimatedBpm < 70)
            {
                estimatedBpm *= 2.0;
            }

            firstBeatMs = 0;
        }

        // =====================================================
        // SECTION DETECTION
        // =====================================================

        private static List<TimelineSection> GenerateSections(
            string filePath,
            double totalDurationMs)
        {
            var sections = new List<TimelineSection>();

            int fftSize = 2048;

            List<float[]> spectrogram =
                new List<float[]>();

            List<double[]> bandEnergy =
                new List<double[]>();

            List<double> rmsValues =
                new List<double>();

            int sampleRate;
            int hopSize;

            try
            {
                using (var reader = new AudioFileReader(filePath))
                {
                    sampleRate = reader.WaveFormat.SampleRate;

                    hopSize = (int)(sampleRate * 0.04);

                    int channels = reader.WaveFormat.Channels;

                    float[] readBuffer =
                        new float[hopSize * channels];

                    int read;

                    while ((read = reader.Read(readBuffer, 0, readBuffer.Length)) > 0)
                    {
                        int frames = read / channels;

                        if (frames <= 0)
                            continue;

                        float[] mono = new float[fftSize];

                        double rms = 0;

                        for (int i = 0; i < Math.Min(frames, fftSize); i++)
                        {
                            float sample = 0;

                            for (int ch = 0; ch < channels; ch++)
                            {
                                sample += readBuffer[i * channels + ch];
                            }

                            sample /= channels;

                            mono[i] = sample;

                            rms += sample * sample;
                        }

                        rms =
                            Math.Sqrt(rms / Math.Max(1, frames));

                        rmsValues.Add(rms);

                        NAudio.Dsp.Complex[] fftBuffer =
                            new NAudio.Dsp.Complex[fftSize];

                        for (int i = 0; i < fftSize; i++)
                        {
                            fftBuffer[i].X =
                                mono[i] * Hann(i, fftSize);

                            fftBuffer[i].Y = 0;
                        }

                        NAudio.Dsp.FastFourierTransform.FFT(
                            true,
                            (int)Math.Log(fftSize, 2.0),
                            fftBuffer);

                        float[] mags =
                            new float[fftSize / 2];

                        double low = 0;
                        double mid = 0;
                        double high = 0;

                        double binFreq =
                            (double)sampleRate / fftSize;

                        for (int i = 0; i < fftSize / 2; i++)
                        {
                            double mag =
                                Math.Sqrt(
                                    fftBuffer[i].X * fftBuffer[i].X +
                                    fftBuffer[i].Y * fftBuffer[i].Y);

                            mag =
                                Math.Log10(1.0 + mag * 10.0);

                            mags[i] = (float)mag;

                            double freq = i * binFreq;

                            if (freq < 250)
                                low += mag;
                            else if (freq < 2000)
                                mid += mag;
                            else
                                high += mag;
                        }

                        spectrogram.Add(mags);

                        bandEnergy.Add(new double[]
                        {
                            low,
                            mid,
                            high
                        });
                    }
                }
            }
            catch
            {
                return sections;
            }

            if (spectrogram.Count < 10)
                return sections;

            int countFrames = spectrogram.Count;

            double[] spectralFlux =
                new double[countFrames];

            for (int i = 1; i < countFrames; i++)
            {
                double flux = 0;

                for (int k = 0; k < spectrogram[i].Length; k++)
                {
                    double diff =
                        spectrogram[i][k] -
                        spectrogram[i - 1][k];

                    if (diff > 0)
                        flux += diff;
                }

                spectralFlux[i] = flux;
            }
            double fluxThreshold = 0.15;

            bool[] onsetFlags = new bool[countFrames];

            for (int i = 1; i < countFrames - 1; i++)
            {
                onsetFlags[i] =
                    spectralFlux[i] > spectralFlux[i - 1] &&
                    spectralFlux[i] >= spectralFlux[i + 1] &&
                    spectralFlux[i] > fluxThreshold;
            }

            double[] novelty =
                new double[countFrames];

            int W = 25;

            for (int i = W; i < countFrames - W; i++)
            {
                double rmsBefore = 0;
                double rmsAfter = 0;
                int onsetBefore = 0;
                int onsetAfter = 0;
                for (int j = 0; j < W; j++)
                {
                    rmsBefore += rmsValues[i - W + j];
                    rmsAfter += rmsValues[i + j];
                    if (onsetFlags[i - W + j])
                        onsetBefore++;

                    if (onsetFlags[i + j])
                        onsetAfter++;
                }
                double onsetRateChange = Math.Abs(onsetAfter - onsetBefore) / (double)W;
                rmsBefore /= W;
                rmsAfter /= W;

                double rmsChange =
                    Math.Abs(rmsAfter - rmsBefore) /
                    (Math.Max(rmsBefore, rmsAfter) + 0.03);

                double lowBefore = 0;
                double lowAfter = 0;

                double midBefore = 0;
                double midAfter = 0;

                double highBefore = 0;
                double highAfter = 0;

                for (int j = 0; j < W; j++)
                {
                    lowBefore += bandEnergy[i - W + j][0];
                    lowAfter += bandEnergy[i + j][0];

                    midBefore += bandEnergy[i - W + j][1];
                    midAfter += bandEnergy[i + j][1];

                    highBefore += bandEnergy[i - W + j][2];
                    highAfter += bandEnergy[i + j][2];
                }

                double lowChange =
                    Math.Abs(lowAfter - lowBefore) /
                    (Math.Max(lowBefore, lowAfter) + 0.1);

                double midChange =
                    Math.Abs(midAfter - midBefore) /
                    (Math.Max(midBefore, midAfter) + 0.1);

                double highChange =
                    Math.Abs(highAfter - highBefore) /
                    (Math.Max(highBefore, highAfter) + 0.1);

                double fluxBefore = 0;
                double fluxAfter = 0;

                for (int j = 0; j < W; j++)
                {
                    fluxBefore += spectralFlux[i - W + j];
                    fluxAfter += spectralFlux[i + j];
                }

                double fluxChange =
                    Math.Abs(fluxAfter - fluxBefore) /
                    (Math.Max(fluxBefore, fluxAfter) + 0.3);

                double dot = 0;
                double normA = 0;
                double normB = 0;

                for (int k = 0; k < spectrogram[i].Length; k++)
                {
                    double a = 0;
                    double b = 0;

                    for (int j = 0; j < W; j++)
                    {
                        a += spectrogram[i - W + j][k];
                        b += spectrogram[i + j][k];
                    }

                    dot += a * b;

                    normA += a * a;
                    normB += b * b;
                }

                double cosine =
                    (normA > 0 && normB > 0)
                    ? 1.0 - (dot / Math.Sqrt(normA * normB))
                    : 0;

                novelty[i] =
                       rmsChange * 1.0
                     + lowChange * 0.7
                     + midChange * 0.7
                     + highChange * 0.9
                     + fluxChange * 1.2
                     + onsetRateChange * 2.2
                     + cosine * 0.5;
            }

            double[] smoothNovelty =
                new double[countFrames];

            int smoothWindow = 6;

            for (int i = 0; i < countFrames; i++)
            {
                double sum = 0;
                int cnt = 0;

                for (int j = Math.Max(0, i - smoothWindow);
                         j <= Math.Min(countFrames - 1, i + smoothWindow);
                         j++)
                {
                    sum += novelty[j];
                    cnt++;
                }

                smoothNovelty[i] = sum / cnt;
            }

            List<int> boundaries =
                new List<int>();

            int minDistance =
                (int)(6.0 / 0.04);

            for (int i = W; i < countFrames - W; i++)
            {
                bool isPeak = true;

                for (int d = -8; d <= 8; d++)
                {
                    if (d == 0)
                        continue;

                    if (smoothNovelty[i] < smoothNovelty[i + d])
                    {
                        isPeak = false;
                        break;
                    }
                }

                if (!isPeak)
                    continue;

                double localAvg = 0;

                int c = 0;

                for (int j = Math.Max(0, i - 50);
                         j <= Math.Min(countFrames - 1, i + 50);
                         j++)
                {
                    localAvg += smoothNovelty[j];
                    c++;
                }

                localAvg /= c;

                bool strongEnough =
                    smoothNovelty[i] > localAvg * 1.45 &&
                    smoothNovelty[i] > 0.75;

                if (!strongEnough)
                    continue;

                if (boundaries.Count == 0 ||
                    (i - boundaries.Last()) >= minDistance)
                {
                    boundaries.Add(i);
                }
                else
                {
                    if (smoothNovelty[i] >
                        smoothNovelty[boundaries.Last()])
                    {
                        boundaries[boundaries.Count - 1] = i;
                    }
                }
            }

            List<int> aligned =
                new List<int>();

            foreach (int b in boundaries)
            {
                int bestIdx = b;

                double bestScore = -999999;

                int start = Math.Max(1, b - 4);
                int end = Math.Min(countFrames - 1, b + 6);

                for (int i = start; i <= end; i++)
                {
                    double rmsSpike =
                        rmsValues[i] - rmsValues[i - 1];

                    double fluxSpike =
                        spectralFlux[i];

                    double score =
                        smoothNovelty[i] * 2.5 +
                        rmsSpike * 0.8 +
                        fluxSpike * 0.5;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                    }
                }

                if (aligned.Count == 0 ||
                    (bestIdx - aligned.Last()) >= minDistance)
                {
                    aligned.Add(bestIdx);
                }
            }

            sections.Add(new TimelineSection
            {
                StartTimeMs = 0,
                Name = "",
                Color = Brushes.Transparent
            });

            foreach (int idx in aligned)
            {
                double timeMs =
                    (idx * (double)hopSize / sampleRate) * 1000.0;

                sections.Add(new TimelineSection
                {
                    StartTimeMs = timeMs,
                    Name = "",
                    Color = Brushes.Transparent
                });
            }

            return sections;
        }
    }
}