
namespace MVS.Core
{
    [Serializable]
    public class CameraException : Exception
    {
        /// <summary>
        /// 相机异常
        /// </summary>

            public CameraException() { }
            public CameraException(string message) : base(message) { }
            public CameraException(string message, Exception inner) : base(message, inner) { }
        
    }
}