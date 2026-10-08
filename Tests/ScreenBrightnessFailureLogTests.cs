using System;
using System.Collections.Generic;
using System.Management;
using System.Reflection;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// "Not supported" from the brightness query is an expected state and stays out of the error log;
/// any other failure is still an error.
/// </summary>
public class ScreenBrightnessFailureLogTests
{
    // The error code has no public setter or constructor, so the real exception type is built and
    // the code written to its backing field.
    private static ManagementException WithCode(ManagementStatus code)
    {
        var exception = new ManagementException("test");
        var field = typeof(ManagementException).GetField("errorCode", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("ManagementException no longer keeps its code in errorCode");
        field.SetValue(exception, code);
        Assert.Equal(code, exception.ErrorCode);
        return exception;
    }

    [Fact]
    public void NotSupported_is_one_information_line_per_process_and_never_an_error()
    {
        List<string> infos = [];
        List<string> errors = [];
        var log = new WmiFailureLog(infos.Add, (source, _) => errors.Add(source));

        log.Report("first", WithCode(ManagementStatus.NotSupported));
        log.Report("second", WithCode(ManagementStatus.NotSupported));

        Assert.Single(infos);
        Assert.Empty(errors);
    }

    [Fact]
    public void Any_other_failure_is_an_error_every_time()
    {
        List<string> infos = [];
        List<string> errors = [];
        var log = new WmiFailureLog(infos.Add, (source, _) => errors.Add(source));

        log.Report("access", WithCode(ManagementStatus.AccessDenied));
        log.Report("other", new InvalidOperationException());

        Assert.Equal(["access", "other"], errors);
        Assert.Empty(infos);
    }
}
