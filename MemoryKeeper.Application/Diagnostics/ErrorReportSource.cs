namespace MemoryKeeper.Application.Diagnostics;

public enum ErrorReportSource
{
    Unhandled,
    Startup,
    Gallery,
    Import,
    General
}

public enum ErrorLogTarget
{
    Startup,
    Gallery
}
