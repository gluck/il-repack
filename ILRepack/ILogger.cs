
namespace ILRepacking
{
    public interface ILogger
    {
        bool ShouldLogVerbose { get; set; }

        void Error(string msg);
        void Warn(string msg);
        void Info(string msg);
        void Verbose(string msg);
    }
}
