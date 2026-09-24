using System;

namespace NzbDrone.Core.MediaFiles.Azw
{
    public class AzwTagException : Exception
    {
        public AzwTagException(string message)
            : base(message)
        {
        }
    }
}
