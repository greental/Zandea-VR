using System;
using System.Collections.Generic;
using UnityEngine;

namespace JunkerScanner
{
    public enum ScanStatus { Unknown, Operational, Damaged, Fault, Destroyed, Missing, Salvageable, Repairable, Locked, Offline }
    public enum ScanHighlightKind { NormalScan, Interaction, Salvage, Cut, InstallationTarget, Fault }
    public enum DiagnosticSeverity { Information, Warning, Critical }

    public sealed class ScanInfo
    {
        public string Name, Category, Description, Hint;
        public ScanStatus Status;
        public float? Condition;
        public readonly List<DiagnosticReading> Diagnostics = new();
    }

    public readonly struct DiagnosticReading
    {
        public readonly string Name, Message;
        public readonly ScanStatus Status;
        public readonly DiagnosticSeverity Severity;
        public readonly Transform Location;
        public readonly ScanHighlightTarget Highlight;
        public DiagnosticReading(string name, ScanStatus status, DiagnosticSeverity severity, string message,
            Transform location, ScanHighlightTarget highlight)
        { Name = name; Status = status; Severity = severity; Message = message; Location = location; Highlight = highlight; }
    }

    public interface IScannable
    {
        bool CanScan { get; }
        int Priority { get; }
        ScanInfo Inspect();
    }

    public interface IScanPathProvider
    {
        IEnumerable<AuthoredCutPath> ScannerPaths { get; }
    }

    // Additional domains provide data without depending on the scanner or its UI.
    public abstract class ScanInfoProvider : MonoBehaviour
    {
        public abstract void Populate(ScanInfo info);
    }

    public static class ScanStatusUtility
    {
        public static bool IsFault(ScanStatus status) => status == ScanStatus.Fault || status == ScanStatus.Damaged ||
            status == ScanStatus.Destroyed || status == ScanStatus.Missing || status == ScanStatus.Repairable;
        public static string Label(ScanStatus status) => status == ScanStatus.Operational ? "OK" : status.ToString().ToUpperInvariant();
    }
}
