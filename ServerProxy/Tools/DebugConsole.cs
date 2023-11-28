using System.Runtime.InteropServices;

namespace ServerProxy.Tools;

internal static class DebugConsole
{
	/// <summary>
	///     Allocates a new console for current process.
	/// </summary>
	[DllImport("kernel32.dll")]
	public static extern bool AllocConsole();

	/// <summary>
	///     Frees the console.
	/// </summary>
	[DllImport("kernel32.dll")]
	public static extern bool FreeConsole();
}