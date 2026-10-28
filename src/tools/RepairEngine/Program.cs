using System;
using System.IO;
using System.Threading.Tasks;
using SSF2ModManager.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: RepairEngine <prepared-install-root>");
    return 1;
}

var root = args[0];
if (!Directory.Exists(root))
{
    Console.Error.WriteLine("Install root not found: " + root);
    return 1;
}

var svc = new GamePrepareService();
var result = await svc.RepairEngineAsync(
    root,
    jpexsPath: null,
    progress: new Progress<string>(m => Console.WriteLine(m)));
Console.WriteLine(result.Success ? "OK: " + result.Message : "FAIL: " + result.Message);
return result.Success ? 0 : 2;
