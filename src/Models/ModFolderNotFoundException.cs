namespace SSF2ModManager.Models
{
    public class ModFolderNotFoundException : Exception
    {
        public InstalledMod Mod { get; }
        public string ModPath { get; }

        public ModFolderNotFoundException(InstalledMod mod, string modPath)
            : base($"Mod folder not found: {modPath}")
        {
            Mod = mod;
            ModPath = modPath;
        }
    }
}
