using System.Runtime.CompilerServices;

// Allows the offline Windows specs to exercise a loopback-only receiver seam.
// Production callers can still bind only to explicit RFC1918 interfaces.
[assembly: InternalsVisibleTo("Rift.Automation.Windows.Specs")]
