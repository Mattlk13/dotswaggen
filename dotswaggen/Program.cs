﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using CommandLine;
using dotswaggen.CSharpModel;
using dotswaggen.Interfaces;
using dotswaggen.Swagger;
using DotLiquid;
using Newtonsoft.Json;

namespace dotswaggen
{
    internal class Program
    {
        private static Options _options;
        private static Dictionary<string, Type> _converterRegistry;

        private static void Main(string[] args)
        {
            // Load command line options
            _options = new Options();
            var result = Parser.Default.ParseArgumentsStrict(args, _options, () => _options.GetUsage());
            if (!result)
            {
                return;
            }

            // Populate ConverterRegistry
            _converterRegistry = new Dictionary<string, Type> {{"c#", typeof (SwaggerConverter)}};

            // TODO: Allow multiple files or input file directory
            ProcessFile(_options.InputFile);
        }

        private static void ProcessFile(string inputFile)
        {
            // Load json file
            var filename = Path.GetFileNameWithoutExtension(inputFile);
            string json;
            using (var webClient = new WebClient())
            {
                if ( !string.IsNullOrEmpty(_options.Username) )
                    webClient.Credentials = new System.Net.NetworkCredential(_options.Username, _options.Password ?? string.Empty);
                json = webClient.DownloadString(inputFile);
            }

            try
            {
                var converter = LoadConverter(json, _converterRegistry[_options.Model]);

                converter.RegisterSafeTypes();

                foreach (var m in converter.Models)
                {
                    var typeFileModel = new ClassFile
                    {
                        Resourceurl = inputFile,
                        Namespace = _options.Namespace,
                        DataType = m
                    };

                    WriteFile(ApplyTemplate(GetTemplate("Model"), typeFileModel), m.Name, converter.DefaultExtension);
                }

                var operationFileModel = new OperationsFile
                {
                    Resourceurl = inputFile,
                    Namespace = _options.Namespace,
                    Name = filename ?? "OutputClass",
                    Apis = converter.Apis
                };

                WriteFile(ApplyTemplate(GetTemplate("Action"), operationFileModel), operationFileModel.Name,
                    converter.DefaultExtension);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static BaseSwaggerConverter LoadConverter(string json, Type type)
        {
            var swaggerResource = LoadSwagger(json);

            if (swaggerResource.Apis == null)
                throw new Exception("Could not load JSON as Swagger document");

            var converterInstance = (BaseSwaggerConverter) Activator.CreateInstance(type, swaggerResource);
            return converterInstance;
        }

        private static ApiDeclaration LoadSwagger(string json)
        {
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                Error = (sender, args) => { Console.WriteLine(args.ErrorContext.Error.Message); }
            };

            // do some nasty hacks here because Json.NET reserves '$' for internal stuff
            json = json.Replace("$ref", "ref");

            // Parse Models
            var swaggerResource = JsonConvert.DeserializeObject<ApiDeclaration>(json, settings);
            return swaggerResource;
        }

        private static void WriteFile(string renderedCode, string fileName, string extension)
        {
            Directory.CreateDirectory(_options.OutputFolder);
            if (!string.IsNullOrEmpty(_options.WriteSingleFileName))
            {
                using (
                    var outFile =
                        File.AppendText(Path.Combine(_options.OutputFolder,
                            string.Format("{0}", _options.WriteSingleFileName))))
                {
                    outFile.Write(renderedCode);
                }
            }
            else
            {
                using (
                    var outFile =
                        File.CreateText(Path.Combine(_options.OutputFolder,
                            string.Format("{0}{1}.{2}", _options.OutputPrefix, fileName, extension))))
                {
                    outFile.Write(renderedCode);
                }
            }
        }

        private static string GetSubType(ApiDeclaration swaggerResource, string subTypeName)
        {
            var subType =
                swaggerResource.Models.SingleOrDefault(
                    x => x.Value.SubTypes != null && x.Value.SubTypes.Contains(subTypeName)).Key;
            return subType;
        }

        private static Template GetTemplate(string name)
        {
            var template2 =
                Template.Parse(
                    File.ReadAllText(string.Format("Templates\\{0}{1}Template.txt", _options.TemplatePrefix, name)));
            return template2;
        }

        private static string ApplyTemplate<TMODEL>(Template template, TMODEL model)
        {
            return template.Render(Hash.FromAnonymousObject(new
            {
                Model = model
            }));
        }
    }

    public class TemplateProperties : Drop
    {
        public string Resourceurl { get; set; }
        public string Namespace { get; set; }
    }

    public class OperationsFile : TemplateProperties
    {
        public string Name { get; set; }
        public IApi[] Apis { get; set; }
    }

    public class ClassFile : TemplateProperties
    {
        public IDataType DataType { get; set; }
    }
}