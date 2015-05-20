
namespace ILRepacking
{
    public interface ILogger
    {
        bool ShouldLogVerbose { get; set; }

        void Log(object str);

        void ERROR(string msg);

        void WARN(string msg);

        void INFO(string msg);

        void VERBOSE(string msg);

        void DuplicateIgnored(string ignoredType, object ignoredObject);
    }
}
