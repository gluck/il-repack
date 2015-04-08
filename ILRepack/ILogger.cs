
namespace ILRepacking
{
    public interface ILogger
    {
        bool ShouldLogVerbose { get; set; }

        void Log(object str);

        bool Open(string outputFile);

        void Close();

        void ERROR(string msg);

        void WARN(string msg);

        void INFO(string msg);

        void VERBOSE(string msg);

        void DuplicateIgnored(string ignoredType, object ignoredObject);
    }
}
