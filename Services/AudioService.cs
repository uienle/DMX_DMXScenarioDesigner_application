using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace DMXScenarioDesigner.Services

{
    public class AudioService : IDisposable
    {
        // Audio output device
        private WaveOutEvent _outputDevice;

        // Audio file reader
        private AudioFileReader _audioFile;

        /// <summary>
        /// Load an mp3 or wav file
        /// </summary>
        public void Load(string filePath)
        {
            // Clean old audio if already loaded
            DisposeAudio();

            // Create new audio reader
            _audioFile = new AudioFileReader(filePath);

            // Create output device
            _outputDevice = new WaveOutEvent();

            // Connect audio reader to output device
            _outputDevice.Init(_audioFile);
        }

        /// <summary>
        /// Play audio
        /// </summary>
        public void Play()
        {
            _outputDevice?.Play();
        }

        /// <summary>
        /// Pause audio
        /// </summary>
        public void Pause()
        {
            _outputDevice?.Pause();
        }

        /// <summary>
        /// Stop audio and return to beginning
        /// </summary>
        public void Stop()
        {
            if (_outputDevice != null)
            {
                _outputDevice.Stop();
            }

            if (_audioFile != null)
            {
                _audioFile.CurrentTime = TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Jump to a specific time in the audio
        /// </summary>
        public void Seek(TimeSpan time)
        {
            if (_audioFile != null)
            {
                _audioFile.CurrentTime = time;
            }
        }

        /// <summary>
        /// Current playback time
        /// </summary>
        public TimeSpan CurrentTime
        {
            get
            {
                if (_audioFile != null)
                {
                    return _audioFile.CurrentTime;
                }

                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Total audio duration
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                if (_audioFile != null)
                {
                    return _audioFile.TotalTime;
                }

                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Current playback state
        /// </summary>
        public PlaybackState PlaybackState
        {
            get
            {
                if (_outputDevice != null)
                {
                    return _outputDevice.PlaybackState;
                }

                return PlaybackState.Stopped;
            }
        }

        /// <summary>
        /// Clean up audio resources
        /// </summary>
        private void DisposeAudio()
        {
            _outputDevice?.Stop();
            _outputDevice?.Dispose();
            _outputDevice = null;

            _audioFile?.Dispose();
            _audioFile = null;
        }

        /// <summary>
        /// Dispose everything
        /// </summary>
        public void Dispose()
        {
            DisposeAudio();
        }
    }
}