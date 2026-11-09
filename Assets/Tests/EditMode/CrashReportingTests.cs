using BumperTanks.Frontend;
using NUnit.Framework;
using UnityEngine;

namespace BumperTanks.Tests
{
    public sealed class CrashReportingTests
    {
        [Test]
        public void ReporterRequiresExceptionLogAndExplicitConsent()
        {
            Assert.That(CrashReporter.ShouldCapture(LogType.Exception, false), Is.False);
            Assert.That(CrashReporter.ShouldCapture(LogType.Error, true), Is.False);
            Assert.That(CrashReporter.ShouldCapture(LogType.Warning, true), Is.False);
            Assert.That(CrashReporter.ShouldCapture(LogType.Exception, true), Is.True);
        }

        [Test]
        public void FingerprintIsDigestAndIgnoresMessageAndSourceLineDetails()
        {
            var first = CrashFingerprint.Create("System.InvalidOperationException: private details", "at Game.Run() in C:/secret/source.cs:line 42\nat Game.Tick()");
            var sameSignature = CrashFingerprint.Create("System.InvalidOperationException: different private details", "at Game.Run() in D:/other/source.cs:line 99\nat Game.Tick()");

            Assert.That(first, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(first, Is.EqualTo(sameSignature));
            Assert.That(first, Does.Not.Contain("private"));
        }

        [Test]
        public void FingerprintChangesWhenExceptionTypeOrFrameChanges()
        {
            var baseline = CrashFingerprint.Create("System.InvalidOperationException: private", "at Game.Run()");

            Assert.That(CrashFingerprint.Create("System.ArgumentException: private", "at Game.Run()"), Is.Not.EqualTo(baseline));
            Assert.That(CrashFingerprint.Create("System.InvalidOperationException: private", "at Game.Other()"), Is.Not.EqualTo(baseline));
        }
    }
}
