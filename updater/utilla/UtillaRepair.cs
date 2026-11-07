using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TotemMod.Updating.UtillaFix
{
    internal static class UtillaRepair
    {
        private const string OriginalHash="4C64753E6F3609A40CA5D86E6033B134F915474EC7387D3FD517A95F255DE813";
        private const string GameHash="FF7337A447B80B0FCC1738C79A98FB9DE2C72497CC68C8588FD822359E06E4A7";
        private const string Guid="org.legoandmars.gorillatag.utilla";

        internal static string Apply(string bepinRoot,string managedDirectory,byte[] payload)
        {
            string root=Path.GetFullPath(bepinRoot),plugins=Path.Combine(root,"plugins");
            SafePath(root); SafePath(plugins);
            if(!Directory.Exists(plugins)) return "Utilla repair skipped: plugins directory is missing.";
            string cache=Path.Combine(root,"cache"); EnsureDirectory(cache);
            cache=Path.Combine(cache,"TotemModUtilla"); EnsureDirectory(cache);
            string lockPath=Path.Combine(cache,"repair.lock"); SafePath(lockPath);
            using(FileStream repairLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                List<string> candidates=Find(plugins);
                if(candidates.Count!=1) return "Utilla repair skipped: found "+candidates.Count+" Utilla identities; exactly one is required.";
                string target=candidates[0]; SafePath(target);
                using(FileStream installed=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                {
                    byte[] before=Read(installed,2*1024*1024);
                    if(IsPatched(before)) return "Utilla is already compatible; no files changed.";
                    if(Hash(before)!=OriginalHash) return "Utilla repair skipped: unsupported Utilla version; installed file retained.";
                    string game=Path.Combine(Path.GetFullPath(managedDirectory),"Assembly-CSharp.dll"); SafePath(game);
                    using(FileStream source=new FileStream(game,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(SHA256 sha=SHA256.Create())
                        if(Hex(sha.ComputeHash(source))!=GameHash) return "Utilla repair skipped: unsupported game build; installed file retained.";
                    if(payload==null || payload.Length==0 || payload.Length>1024*1024) throw new InvalidDataException("Invalid Utilla compatibility payload size.");
                    byte[] patched=PatchUtilla.Apply(before,payload,managedDirectory);
                    if(!IsPatched(patched)) throw new InvalidDataException("Utilla compatibility projection was not installed.");
                    string backups=Path.Combine(cache,"backups"); EnsureDirectory(backups);
                    string suffix=System.Guid.NewGuid().ToString("N");
                    string temporary=Path.Combine(cache,"repair-"+suffix+".tmp");
                    string backup=Path.Combine(backups,"Utilla-"+OriginalHash+"-"+suffix+".bak");
                    try
                    {
                        WriteNew(temporary,patched);
                        SafePath(target); SafePath(backup); SafePath(temporary);
                        // Keep the inspected handle open with writes denied. A second
                        // reader confirms the pathname still names the known original.
                        using(FileStream current=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                            if(Hash(Read(current,2*1024*1024))!=OriginalHash) throw new IOException("Utilla changed during repair.");
                        File.Replace(temporary,target,backup);
                    }
                    finally
                    {
                        if(File.Exists(temporary)) { SafePath(temporary); File.Delete(temporary); }
                    }
                    return "Installed Utilla compatibility fix; exact original saved in "+backups+".";
                }
            }
        }

        private static List<string> Find(string plugins)
        {
            List<string> found=new List<string>();
            Stack<KeyValuePair<string,int>> pending=new Stack<KeyValuePair<string,int>>();
            pending.Push(new KeyValuePair<string,int>(plugins,0));
            int directories=0,files=0; long inspected=0;
            while(pending.Count>0)
            {
                KeyValuePair<string,int> entry=pending.Pop(); SafePath(entry.Key);
                if(++directories>1024 || entry.Value>16) throw new InvalidDataException("Utilla scan directory limit exceeded.");
                foreach(string child in Directory.GetDirectories(entry.Key))
                    if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0) pending.Push(new KeyValuePair<string,int>(child,entry.Value+1));
                foreach(string file in Directory.GetFiles(entry.Key,"*.dll"))
                {
                    if(++files>2048) throw new InvalidDataException("Utilla scan file limit exceeded.");
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0) continue;
                    SafePath(file);
                    using(FileStream stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
                    {
                        inspected+=stream.Length;
                        if(stream.Length>64*1024*1024 || inspected>512L*1024*1024) throw new InvalidDataException("Utilla scan byte limit exceeded.");
                        try
                        {
                            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(stream))
                                foreach(TypeDefinition type in AllTypes(assembly.MainModule.Types))
                                foreach(CustomAttribute attribute in type.CustomAttributes)
                                    if(attribute.AttributeType.FullName=="BepInEx.BepInPlugin" && attribute.ConstructorArguments.Count==3 &&
                                        string.Equals(attribute.ConstructorArguments[0].Value as string,Guid,StringComparison.Ordinal)) found.Add(file);
                        }
                        catch(BadImageFormatException) { }
                    }
                }
            }
            return found;
        }

        private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
        {
            foreach(TypeDefinition type in types)
            {
                yield return type;
                foreach(TypeDefinition nested in AllTypes(type.NestedTypes)) yield return nested;
            }
        }

        private static bool IsPatched(byte[] bytes)
        {
            using(MemoryStream stream=new MemoryStream(bytes,false))
            using(AssemblyDefinition assembly=AssemblyDefinition.ReadAssembly(stream))
            {
                ModuleDefinition module=assembly.MainModule;
                string[] names={"InfectionWireFormat","InfectionCompatibility","PrivateRoomCreationPatch","PrivateRoomModeUpdatePatch"};
                if(names.Any(name=>module.GetType("Utilla.Compat."+name)==null)) return false;
                TypeDefinition lookup=module.GetType("Utilla.Utils.GameModeUtils"),network=module.GetType("Utilla.Behaviours.UtillaNetworkController");
                if(lookup==null || network==null) return false;
                MethodDefinition find=lookup.Methods.FirstOrDefault(m=>m.Name=="FindGamemodeInString");
                if(find==null || !find.HasBody || !find.Body.Instructions.Any(IsProjection)) return false;
                int projectedReads=0;
                foreach(MethodDefinition method in network.Methods.Where(m=>m.HasBody))
                foreach(Instruction instruction in method.Body.Instructions)
                {
                    MethodReference called=instruction.Operand as MethodReference;
                    if(called!=null && called.DeclaringType.FullName=="NetworkSystem" && called.Name=="get_GameModeString" &&
                        instruction.Next!=null && IsProjection(instruction.Next)) projectedReads++;
                }
                return projectedReads==2;
            }
        }

        private static bool IsProjection(Instruction instruction)
        {
            MethodReference called=instruction.Operand as MethodReference;
            return called!=null && called.DeclaringType.FullName=="Utilla.Compat.InfectionCompatibility" && called.Name=="ProjectLocal";
        }

        internal static void SafePath(string path)
        {
            string current=Path.GetFullPath(path);
            while(!string.IsNullOrEmpty(current))
            {
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Utilla repair refuses a reparse path: "+current);
                current=Path.GetDirectoryName(current);
            }
        }

        private static void EnsureDirectory(string path)
        {
            SafePath(path); Directory.CreateDirectory(path); SafePath(path);
        }

        internal static byte[] Read(Stream stream,int maximum)
        {
            if(stream.Length<1 || stream.Length>maximum) throw new InvalidDataException("Utilla repair input exceeds its permitted size.");
            byte[] bytes=new byte[(int)stream.Length]; int offset=0,read;
            while(offset<bytes.Length && (read=stream.Read(bytes,offset,bytes.Length-offset))>0) offset+=read;
            if(offset!=bytes.Length) throw new EndOfStreamException();
            return bytes;
        }

        private static void WriteNew(string path,byte[] bytes)
        {
            SafePath(path);
            using(FileStream file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { file.Write(bytes,0,bytes.Length); file.Flush(true); }
        }

        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-",""); }
        private static string Hash(byte[] bytes)
        {
            using(SHA256 sha=SHA256.Create()) return Hex(sha.ComputeHash(bytes));
        }
    }
}
