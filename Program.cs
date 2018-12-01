using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using CommandLine;
using CommandLine.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GenCorefxOobLayout
{

    class CommandLineOptions
    {
        [RequiredArgument(0,"corefxDir","Corefx Directory")]
        public string CorefxDirectory { get; set; }

        [RequiredArgument(1, "externalIndex", "Json index containing external dependencies")]
        public string ExternalIndex { get; set; }

        [OptionalArgument("artifacts\\bin\\ref\\netcoreapp", "netcoreappRef", "Corefx's netcoreapp ref path")]
        public string CorefxNetcoreappRef { get; set; }

        [OptionalArgument("PlatformExtensions", "out", "Output Directory")]
        public string OutDir { get; set; }

        [OptionalArgument("netcoreapp3.0", "framework", "Framework to restore external dependencies")]
        public string Framework { get; set; }

        [OptionalArgument("win7-x64", "rid", "Runtime Identifier")]
        public string Rid { get; set; }

        [OptionalArgument("2.1", "runtimeVersion", "Runtime Framework Version")]
        public string RuntimeVersion { get; set; }

        [OptionalArgument("dotnet", "dotnetcli", "Dotnet CLI Path")]
        public string DotnetCli { get; set; }

        [OptionalArgument("https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json;https://api.nuget.org/v3/index.json", "restoreSources", "NuGet restore sources separated by ;")]
        public string RestoreSources { get; set; }

        [OptionalArgument(false, "justExternal", "Restore just external index")]
        public bool JustExternal { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (!Parser.TryParse(args, out CommandLineOptions options))
            {
                return;
            }

            new AssemblyDropGenerator(options).Generate();
        }
    }

    class AssemblyDropGenerator
    {
        private string _corefxDirectory;
        private string _outDir;
        private string _framework;
        private string _rid;
        private string _dotnetcli;
        private string _restoreSources;
        private string _runtimeVersion;
        private string _netcoreappRefFolder;
        private string _externalIndex;
        private bool _justExternal;
        public AssemblyDropGenerator(CommandLineOptions options)
        {
            _corefxDirectory = options.CorefxDirectory;
            _outDir = options.OutDir;
            Directory.CreateDirectory(_outDir);

            _framework = options.Framework;
            _rid = options.Rid;
            _dotnetcli = options.DotnetCli;
            _restoreSources = options.RestoreSources;
            _runtimeVersion = options.RuntimeVersion;
            _netcoreappRefFolder = options.CorefxNetcoreappRef;
            _externalIndex = options.ExternalIndex;
            _justExternal = options.JustExternal;
        }

        public void Generate()
        {
            if (!_justExternal)
            {
                string[] files = Directory.GetFiles(Path.Join(_corefxDirectory, "src"), "*.pkgproj", SearchOption.AllDirectories);
                Console.WriteLine("==== Copying OOB assemblies ====");
                Console.WriteLine("");
                foreach (var file in files)
                {
                    if (file.Contains("Native") || file.Contains("Private")) continue;

                    if (IsOOB(file))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        fileName = fileName + ".dll";
                        string sourceFile = Path.Combine(_corefxDirectory, _netcoreappRefFolder, fileName);
                        string destName = Path.Combine(_outDir, fileName);
                        if (File.Exists(sourceFile))
                        {
                            Console.WriteLine($"{sourceFile} -> {destName}");
                            File.Copy(sourceFile, destName, overwrite: true);
                        }
                    }
                }
            }

            GenerateCompatPackExternalLayout();
        }

        private bool IsOOB(string pkgproj)
        {
            string projectRootPath = Path.GetDirectoryName(Path.GetDirectoryName(pkgproj));
            string[] file = Directory.GetFiles(projectRootPath, "dir*.props", SearchOption.TopDirectoryOnly);

            JObject propertyGroup = GetPropertyGroup(file[0], out JObject project);
            while (propertyGroup == null)
            {
                // There are cases where we share Directory.Build.props files among projects that share assemblyversion
                // and package version, for example System.Composition.AttributedModel
                JObject import = project["Import"]?.Value<JObject>();
                if (import == null)
                    return true;

                string pathToImport = import["@Project"].Value<string>();
                if (string.IsNullOrEmpty(pathToImport) && !pathToImport.Contains("Directory.Build"))
                    return true;
                
                pathToImport = Path.Combine(projectRootPath, pathToImport);
                propertyGroup = GetPropertyGroup(pathToImport, out project);
            }
            
            JToken isNetCoreAppRef = propertyGroup["IsNETCoreAppRef"];
            if (isNetCoreAppRef != null && !(isNetCoreAppRef.Value<bool>()))
            {
                return true;
            }

            JToken isNetCoreApp = propertyGroup["IsNETCoreApp"];
            if (isNetCoreApp == null)
            {
                return true;
            }

            if (isNetCoreApp.Value<bool>())
            {
                return false;
            }

            return true;
        }

        private JObject GetPropertyGroup(string file, out JObject project)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(file);
            string jsonText = JsonConvert.SerializeXmlNode(doc);
            JObject json = JObject.Parse(jsonText);
            project = json["Project"].Value<JObject>();
            return project["PropertyGroup"]?.Value<JObject>();
        }

        private void GenerateCompatPackExternalLayout()
        {
            List<PackageReference> references = new List<PackageReference>();
            JObject json = JObject.Parse(File.ReadAllText(_externalIndex));
            JToken packages = json["Packages"];
            foreach (JToken child in packages.Children())
            {
                JProperty property = child.ToObject<JProperty>();
                string version = property.Value<JToken>().ToObject<string>();
                references.Add(new PackageReference{
                    Name = property.Name,
                    Version = version
                });
            }

            if (references.Count <= 0)
            {
                return;
            }

            string projectDir = "obj\\generated";
            string projectName = "generated.csproj";

            GenerateCsproj(references, projectDir, projectName);

            string publishDir = PublishProject(Path.Combine(projectDir, projectName));
            CopyExternalDependencies(publishDir, references);
        }

        private void CopyExternalDependencies(string publishDir, List<PackageReference> references)
        {
            Console.WriteLine("");
            Console.WriteLine("==== Copy external dependencies ====");
            Console.WriteLine("");

            // Copy direct package references.
            foreach (var reference in references)
            {
                string fileName = reference.Name + ".dll";
                string source = Path.Combine(publishDir, fileName);
                // We could use a meta package that doesn't contain binaries.
                if (File.Exists(source))
                {
                    string destination = Path.Combine(_outDir, fileName);
                    Console.WriteLine($"{source} -> {destination}");
                    File.Copy(source, destination, overwrite: true);
                }
            }

            // Copy transitive references that are not part of corefx.
            string[] files = Directory.GetFiles(publishDir, "*.dll");
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                string repoRefFile = Path.Combine(_corefxDirectory, _netcoreappRefFolder, fileName);
                string destination = Path.Combine(_outDir, fileName);
                if (!File.Exists(destination) && !File.Exists(repoRefFile))
                {
                    Console.WriteLine($"{file} -> {destination}");
                    File.Copy(file, destination, overwrite: true);
                }
            }
        }

        private void GenerateCsproj(List<PackageReference> packageReferences, string outDir, string name)
        {
            Console.WriteLine("");
            Console.WriteLine("===== Generating csproj to restore external dependencies =====");
            Directory.CreateDirectory(outDir);

            string outCsproj = Path.Combine(outDir, name);
            string content = 
                @"<Project Sdk=""Microsoft.NET.Sdk"">
                    <PropertyGroup>
                        <PreserveCompilationContext>true</PreserveCompilationContext>
                        <!-- Setting NETCoreAppMaximumVersion to a high version so that the sdk doesn't complain if we're restoring/publishing for a higher version than the sdk. -->
                        <NETCoreAppMaximumVersion>99.9</NETCoreAppMaximumVersion>
                    </PropertyGroup>
                  </Project>";

            XDocument document = XDocument.Parse(content);
            XElement root = document.Root;

            XElement propertyGroup = root.Element("PropertyGroup");
            propertyGroup.Add(new XElement("TargetFramework", _framework));
            propertyGroup.Add(new XElement("RuntimeIdentifier", _rid));
            propertyGroup.Add(new XElement("RuntimeFrameworkVersion", _runtimeVersion));

            XElement itemGroup = new XElement("ItemGroup");
            foreach (var packageReference in packageReferences)
            {
                var item = new XElement("PackageReference",
                        new XAttribute("Include", packageReference.Name),
                        new XAttribute("Version", packageReference.Version));
                itemGroup.Add(item);
            }

            root.Add(itemGroup);

            using (FileStream stream = File.Create(outCsproj))
            {
                document.Save(stream);
            }

            Console.WriteLine("");
            Console.WriteLine($"Generated csproj -> {outCsproj}");
        }

        private string PublishProject(string pathToProject)
        {
            Console.WriteLine("");
            Console.WriteLine("==== Publishing external dependencies ====");
            string outDir = "publish";
            ProcessStartInfo psi = new ProcessStartInfo(){
                FileName = _dotnetcli,
                Arguments = $"publish {pathToProject} -o {outDir} /p:RestoreSources={_restoreSources}"
            };

            psi.EnvironmentVariables["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            psi.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"] = "0";

            Console.WriteLine("");
            Console.WriteLine($"{psi.FileName} {psi.Arguments}");
            Console.WriteLine("");

            var p = Process.Start(psi);
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new Exception("Error when publishing generated project");
            
            return Path.Combine(Path.GetDirectoryName(pathToProject), "publish", "refs");
        }
        
        class PackageReference
        {
            public string Name { get; set; }
            public string Version { get; set; }
        }
    }
}
