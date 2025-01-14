using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.WebRTC
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="renderer"></param>
    public delegate void OnAudioReceived(AudioClip renderer);

    /// <summary>
    /// 
    /// </summary>
    public class AudioStreamTrack : MediaStreamTrack
    {
        /// <summary>
        /// 
        /// </summary>
        public event OnAudioReceived OnAudioReceived;

        /// <summary>
        /// 
        /// </summary>
        public AudioSource Source { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public AudioClip Renderer
        {
            get { return _streamRenderer.clip; }
        }


        internal class AudioStreamRenderer : IDisposable
        {
            private AudioClip m_clip;
            private int m_sampleRate;
            private int m_position = 0;
            private int m_channel = 0;

            public AudioClip clip
            {
                get
                {
                    return m_clip;
                }
            }

            public AudioStreamRenderer(string name, int sampleRate, int channels)
            {
                m_sampleRate = sampleRate;
                m_channel = channels;
                int lengthSamples = m_sampleRate;  // sample length for a second

                // note:: OnSendAudio and OnAudioSetPosition callback is called before complete the constructor.
                m_clip = AudioClip.Create(name, lengthSamples, channels, m_sampleRate, false);
            }

            public void Dispose()
            {
                if(m_clip != null)
                    Object.Destroy(m_clip);
                m_clip = null;
            }

            internal void SetData(float[] data)
            {
                int length = data.Length / m_channel;

                if (m_position + length > m_clip.samples)
                {
                    int remain = m_position + length - m_clip.samples;
                    length = m_clip.samples - m_position;

                    // Split two arrays from original data
                    float[] _data = new float[length * m_channel];
                    Buffer.BlockCopy(data, 0, _data, 0, length * m_channel);
                    float[] _data2 = new float[remain * m_channel];
                    Buffer.BlockCopy(data, length * m_channel, _data2, 0, remain * m_channel);

                    // push the split array to the audio buffer
                    SetData(_data);

                    data = _data2;
                    length = remain;
                }
                m_clip.SetData(data, m_position);
                m_position += length;

                if (m_position == m_clip.samples)
                {
                    m_position = 0;
                }
            }
        }

        internal static List<AudioStreamTrack> tracks = new List<AudioStreamTrack>();

        readonly int _sampleRate = 0;
        readonly AudioSourceRead _audioSourceRead;

        private AudioStreamRenderer _streamRenderer;

        /// <summary>
        /// 
        /// </summary>
        public AudioStreamTrack() : this(WebRTC.Context.CreateAudioTrack(Guid.NewGuid().ToString()))
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        public AudioStreamTrack(AudioSource source) : this()
        {
            if (source == null)
                throw new ArgumentNullException("AudioSource argument is null");
            if (source.clip == null)
                throw new ArgumentException("AudioClip must to be attached on AudioSource");
            Source = source;

            _audioSourceRead = source.gameObject.AddComponent<AudioSourceRead>();
            _audioSourceRead.hideFlags = HideFlags.HideInHierarchy;
            _audioSourceRead.onAudioRead += OnSendAudio;
            _sampleRate = Source.clip.frequency;
        }

        internal AudioStreamTrack(IntPtr ptr) : base(ptr)
        {
            tracks.Add(this);
            WebRTC.Context.AudioTrackRegisterAudioReceiveCallback(self, OnAudioReceive);
        }

        /// <summary>
        /// 
        /// </summary>
        public override void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            if (self != IntPtr.Zero && !WebRTC.Context.IsNull)
            {
                tracks.Remove(this);
                if(_audioSourceRead != null)
                    Object.Destroy(_audioSourceRead);
                _streamRenderer?.Dispose();
                WebRTC.Context.AudioTrackUnregisterAudioReceiveCallback(self);
                WebRTC.Context.DeleteMediaStreamTrack(self);
                WebRTC.Table.Remove(self);
                self = IntPtr.Zero;
            }

            this.disposed = true;
            GC.SuppressFinalize(this);
        }

        private void OnSendAudio(float[] data, int channels)
        {
            NativeMethods.ProcessAudio(self, data, _sampleRate, channels, data.Length);
        }

        private void OnAudioReceivedInternal(float[] audioData, int sampleRate, int channels, int numOfFrames)
        {
            if (_streamRenderer == null)
            {
                _streamRenderer = new AudioStreamRenderer(this.Id, sampleRate, channels);

                OnAudioReceived?.Invoke(_streamRenderer.clip);
            }
            _streamRenderer.SetData(audioData);
        }

        [AOT.MonoPInvokeCallback(typeof(DelegateAudioReceive))]
        static void OnAudioReceive(
            IntPtr ptrTrack, float[] audioData, int size, int sampleRate, int numOfChannels, int numOfFrames)
        {
            WebRTC.Sync(ptrTrack, () =>
            {
                if (WebRTC.Table[ptrTrack] is AudioStreamTrack track)
                {
                    track.OnAudioReceivedInternal(audioData, sampleRate, numOfChannels, numOfFrames);
                }
            });
        }
    }
}
