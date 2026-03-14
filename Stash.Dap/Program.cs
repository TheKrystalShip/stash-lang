namespace Stash.Dap;

using System.Threading.Tasks;

public class Program
{
    public static async Task Main(string[] args)
    {
        await StashDebugServer.RunAsync();
    }
}
