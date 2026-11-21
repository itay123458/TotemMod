using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

namespace TotemMod.Updating
{
    internal static class CompanionBootstrap
    {
        private const string Name="TotemMod.CompanionUpdater.dll";
        private const string PrimaryName="TotemMod.Updater.dll";
        internal static string EnsurePrimaryInstalled(string bepinRoot)
        {
            return InstallEmbedded(bepinRoot,PrimaryName);
        }
        internal static string EnsureInstalled(string bepinRoot)
        {
            return InstallEmbedded(bepinRoot,Name);
        }
        private static string InstallEmbedded(string bepinRoot,string fileName)
        {
            Assembly assembly=Assembly.GetExecutingAssembly();
            return InstallHelper(bepinRoot,fileName,Resource(assembly,fileName,1024*1024),Encoding.ASCII.GetString(Resource(assembly,Path.ChangeExtension(fileName,"sha256"),128)).Trim());
        }
        internal static string Install(string bepinRoot,byte[] helper,string expected)
        {
            return InstallHelper(bepinRoot,Name,helper,expected);
        }
        private static string InstallHelper(string bepinRoot,string fileName,byte[] helper,string expected)
        {
            Validate(helper,expected,fileName);
            string root=Path.GetFullPath(bepinRoot),patchers=Path.Combine(root,"patchers");
            Safe(root); Safe(patchers);
            string target=Path.Combine(patchers,fileName); Safe(target);
            if(File.Exists(target)) return Same(target,expected) ? null : "Existing updater retained; install the latest helper while the game is closed.";
            string cache=Path.Combine(root,"cache"); Ensure(cache);
            cache=Path.Combine(cache,"CompanionUpdaterBootstrap"); Ensure(cache);
            string gate=Path.Combine(cache,"bootstrap.lock"); Safe(gate);
            using(FileStream mutex=new FileStream(gate,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                Ensure(patchers); Safe(target);
                if(File.Exists(target)) return Same(target,expected) ? null : "Existing updater retained.";
                string temporary=Path.Combine(cache,Guid.NewGuid().ToString("N")+".tmp"); Safe(temporary);
                try
                {
                    using(FileStream output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    { output.Write(helper,0,helper.Length); output.Flush(true); }
                    Safe(patchers); Safe(target);
                    try { File.Move(temporary,target); }
                    catch(IOException) { if(File.Exists(target)) return Same(target,expected) ? null : "Existing updater retained."; throw; }
                    return fileName==PrimaryName ? "Primary updater ready; restart Gorilla Tag" : "Companion updater ready; restart Gorilla Tag";
                }
                finally { if(File.Exists(temporary)) { Safe(temporary); File.Delete(temporary); } }
            }
        }
        private static void Validate(byte[] helper,string expected,string fileName)
        {
            if(helper==null || helper.Length==0 || helper.Length>1024*1024 || expected==null || expected.Length!=64 ||
                !string.Equals(Hash(helper),expected,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Companion helper hash is invalid.");
            using(MemoryStream stream=new MemoryStream(helper,false))
            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(stream))
            {
                TypeDefinition type=assembly.MainModule.GetType(fileName==PrimaryName ? "TotemMod.Updating.Preloader.Patcher" : "TotemMod.Updating.CompanionPreloader.Patcher");
                bool initialize=false,patch=false,targets=false;
                if(type!=null) foreach(MethodDefinition method in type.Methods)
                {
                    if(!method.IsPublic || !method.IsStatic) continue;
                    initialize|=method.Name=="Initialize" && method.Parameters.Count==0 && method.ReturnType.FullName=="System.Void";
                    patch|=method.Name=="Patch" && method.Parameters.Count==1 && method.Parameters[0].ParameterType.FullName=="Mono.Cecil.AssemblyDefinition";
                    targets|=method.Name=="get_TargetDLLs" && method.Parameters.Count==0;
                }
                if(assembly.Name.Name!=Path.GetFileNameWithoutExtension(fileName) || !initialize || !patch || !targets ||
                    assembly.MainModule.GetType(fileName==PrimaryName ? "TotemMod.Updating.UpdateInstaller" : "TotemMod.Updating.MaceUpdateInstaller")==null)
                    throw new InvalidDataException("Embedded companion helper has an unexpected identity.");
            }
        }
        private static byte[] Resource(Assembly owner,string name,int maximum)
        {
            using(Stream stream=owner.GetManifestResourceStream(name))
            {
                if(stream==null || stream.Length<1 || stream.Length>maximum) throw new InvalidDataException("Missing companion helper resource.");
                byte[] data=new byte[(int)stream.Length]; int offset=0;
                while(offset<data.Length) { int read=stream.Read(data,offset,data.Length-offset); if(read==0) throw new EndOfStreamException(); offset+=read; }
                return data;
            }
        }
        private static bool Same(string path,string expected)
        { Safe(path); using(FileStream file=File.OpenRead(path)) using(SHA256 sha=SHA256.Create()) return file.Length<=1024*1024 && string.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-",""),expected,StringComparison.OrdinalIgnoreCase); }
        private static string Hash(byte[] data) { using(SHA256 sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-",""); }
        private static void Ensure(string path) { Safe(path); Directory.CreateDirectory(path); Safe(path); }
        private static void Safe(string path)
        {
            for(string current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            {
                try { if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new IOException("Companion updater paths cannot be reparse points."); }
                catch(FileNotFoundException) { } catch(DirectoryNotFoundException) { }
            }
        }
    }
}
