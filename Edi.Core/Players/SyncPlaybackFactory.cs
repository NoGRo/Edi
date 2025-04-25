using Edi.Core.Gallery.Definition;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Players
{
    public  class SyncPlaybackFactory(DefinitionRepository _repository)
    {

        public SyncPlayback Create(string galleryName, long seek)
        {
            if (_repository == null)
                throw new InvalidOperationException("SyncPlaybackFactory is not configured.");

            var gallery = _repository.Get(galleryName)
                         ?? throw new ArgumentException($"Gallery not found: {galleryName}");

            return new SyncPlayback(gallery, seek);
        }
        public SyncPlayback Create(DefinitionGallery gallery, long seek)
        {

            return new SyncPlayback(gallery, seek);
        }
    }

    public class SyncPlayback
    {
        private readonly DefinitionGallery _gallery;
        private readonly long _seek;
        private readonly DateTime _sendTime;

        internal SyncPlayback(DefinitionGallery gallery, long seek)
        {
            _gallery = gallery;
            _seek = seek;
            _sendTime = DateTime.Now;
        }

        public string GalleryName => _gallery.Name;
        public DefinitionGallery Gallery => _gallery;
        public long Seek => _gallery.Loop ? _seek % _gallery.Duration: _seek;
        public DateTime SendTime => _sendTime;
        public bool IsLoop => _gallery.Loop;
        public int Duration => _gallery.Duration;

        public long CurrentTime
        {
            get
            {
                var elapsed = (DateTime.Now - _sendTime).TotalMilliseconds;
                var time = _seek + (long)elapsed;

                return _gallery.Loop
                    ? time % _gallery.Duration
                    : Math.Min(time, _gallery.Duration);
            }
        }

        public bool IsFinished => !_gallery.Loop && CurrentTime >= _gallery.Duration;
    }


}