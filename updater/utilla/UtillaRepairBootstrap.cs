using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

namespace TotemMod.Updating
{
    internal static class UtillaRepairBootstrap
    {
        private const string Helper="TotemMod.UtillaFix.dll";
        private const string Retained="An existing different Utilla repair helper was retained; update the helper manually before restarting.";

        internal static string EnsureInstalled(string bepinRoot)
        {
            Assembly owner=Assembly.GetExecutingAssembly();
            byte[] payload=Resource(owner,Helper,1024*1024);
            string expected=Encoding.ASCII.GetString(Resource(owner,"TotemMod.UtillaFix.sha256",128)).Trim();
            Validate(payload,expected);
            string root=Path.GetFullPath(bepinRoot),patchers=Path.Combine(root,"patchers");
            Safe(root); Safe(patchers);
            string target=Path.Combine(patchers,Helper); Safe(target);
            if(File.Exists(target)) return Same(target,expected)?null:Retained;
            string cache=Path.Combine(root,"cache"); Ensure(cache);
            cache=Path.Combine(cache,"TotemModUtilla"); Ensure(cache);
            string lockPath=Path.Combine(cache,"bootstrap.lock"); Safe(lockPath);
            using(FileStream gate=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                Ensure(patchers); Safe(target);
                if(File.Exists(target)) return Same(target,expected)?null:Retained;
                string temporary=Path.Combine(cache,"bootstrap-"+Guid.NewGuid().ToString("N")+".tmp"); Safe(temporary);
                try
                {
                    using(FileStream output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {
                        output.Write(payload,0,payload.Length); output.Flush(true);
                    }
                    Safe(patchers); Safe(target); Safe(temporary);
                    // Move adds a missing file atomically; it never overwrites an
                    // already loaded helper, even if another process just added it.
                    try { File.Move(temporary,target); }
                    catch(IOException) { if(File.Exists(target)) return Same(target,expected)?null:Retained; throw; }
                    return "Utilla repair ready; restart Gorilla Tag";
                }
                finally { if(File.Exists(temporary)) { Safe(temporary); File.Delete(temporary); } }
            }
        }

        internal static void Validate(byte[] payload,string expected)
        {
            if(payload==null || payload.Length<1 || payload.Length>1024*1024 || expected==null || expected.Length!=64 ||
                !string.Equals(Hash(payload),expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded Utilla repair helper digest is invalid.");
            using(MemoryStream stream=new MemoryStream(payload,false))
            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(stream))
            {
                TypeDefinition type=assembly.MainModule.GetType("TotemMod.Updating.UtillaFix.Patcher");
                bool initialize=false,patch=false,targets=false,payloadResource=false;
                if(type!=null) foreach(MethodDefinition method in type.Methods)
                {
                    if(!method.IsPublic || !method.IsStatic) continue;
                    if(method.Name=="Initialize" && method.Parameters.Count==0 && method.ReturnType.FullName=="System.Void") initialize=true;
                    if(method.Name=="Patch" && method.Parameters.Count==1 && method.Parameters[0].ParameterType.FullName=="Mono.Cecil.AssemblyDefinition") patch=true;
                    if(method.Name=="get_TargetDLLs" && method.Parameters.Count==0) targets=true;
                }
                foreach(Resource resource in assembly.MainModule.Resources)
                    if(resource is EmbeddedResource && resource.Name=="TotemMod.UtillaCompat.Payload.dll") payloadResource=true;
                if(assembly.Name.Name!="TotemMod.UtillaFix" || !initialize || !patch || !targets || !payloadResource)
                    throw new InvalidDataException("Embedded DLL is not the expected Utilla repair helper.");
                foreach(AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
                    if(reference.Name.IndexOf("Payload",StringComparison.OrdinalIgnoreCase)>=0)
                        throw new InvalidDataException("Utilla repair helper has an external payload dependency.");
            }
        }

        private static byte[] Resource(Assembly owner,string name,int maximum)
        {
            using(Stream stream=owner.GetManifestResourceStream(name))
            {
                if(stream==null || stream.Length<1 || stream.Length>maximum) throw new InvalidDataException("Embedded Utilla repair resource is missing or invalid: "+name);
                byte[] bytes=new byte[(int)stream.Length]; int offset=0,read;
                while(offset<bytes.Length && (read=stream.Read(bytes,offset,bytes.Length-offset))>0) offset+=read;
                if(offset!=bytes.Length) throw new EndOfStreamException();
                return bytes;
            }
        }

        private static bool Same(string target,string expected)
        {
            Safe(target);
            using(FileStream file=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(SHA256 sha=SHA256.Create())
                return file.Length<=1024*1024 && string.Equals(Hex(sha.ComputeHash(file)),expected,StringComparison.OrdinalIgnoreCase);
        }
        private static string Hash(byte[] bytes) { using(SHA256 sha=SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-",""); }
        private static void Ensure(string path) { Safe(path); Directory.CreateDirectory(path); Safe(path); }
        private static void Safe(string path)
        {
            string current=Path.GetFullPath(path);
            while(!string.IsNullOrEmpty(current))
            {
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Utilla bootstrap refuses a reparse path: "+current);
                current=Path.GetDirectoryName(current);
            }
        }
    }
}
