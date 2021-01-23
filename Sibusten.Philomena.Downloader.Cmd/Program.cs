﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sibusten.Philomena.Downloader.Settings;
using System.CommandLine;
using System.Threading.Tasks;
using Sibusten.Philomena.Api;
using System.Reflection;
using System.CommandLine.Invocation;
using Sibusten.Philomena.Downloader.Cmd.Commands.Arguments;

namespace Sibusten.Philomena.Downloader.Cmd
{
    public static class Program
    {
        private static ConfigAccess configAccess = new ConfigAccess();

        private static string[] GetArgsFromConsole()
        {
            Console.WriteLine("Enter program arguments, one per line. Finish by entering an empty line.");

            List<string> args = new List<string>();
            while (true)
            {
                string? input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                {
                    break;
                }

                args.Add(input);
            }

            return args.ToArray();
        }

        static Command WithHandler(this Command command, string name)
        {
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo? method = typeof(Program).GetMethod(name, flags);

            ICommandHandler handler = CommandHandler.Create(method!);
            command.Handler = handler;
            return command;
        }

        static Command WithSearchQueryArgs(this Command command)
        {
            List<Symbol> SearchQueryArgs = new List<Symbol>
            {
                new Option<string>(new[] { "--query", "-q" }, "The search query"),
                new Option<int>(new[] { "--limit", "-l" }, "The maximum number of images to download. Defaults to all images"),
                new Option<int>(new[] { "--filter", "-f" }, "The ID of the filter to use"),
                new Option<SortField>(new[] { "--sort-field", "-S" }, "How to sort images"),
                new Option<SortDirection>(new[] { "--sort-direction", "-D" }, "The direction to sort images"),
                new Option<string>(new[] { "--image-path", "-I" }, "Where to save images and how to name them"),
                new Option<string>(new[] { "--json-path", "-J" }, "Where to save json files and how to name them"),
                new Option<bool?>(new[] { "--skip-images", "-i" }, "Skip saving images"),
                new Option<bool>(new[] { "--save-json", "-j" }, "Save json metadata files"),
                new Option<bool>(new[] { "--update-json", "-u" }, "Overwrite json metadata files with new data"),
                new Option<List<string>>(new[] { "--boorus", "-b" }, "What booru to download from. Multiple boorus can be given"),
            };

            foreach (Symbol arg in SearchQueryArgs)
            {
                command.Add(arg);
            }

            return command;
        }

        static async Task Main(string[] args)
        {
#if DEBUG
            // Prompt for arguments when debugging if none are given
            if (!args.Any())
            {
                args = GetArgsFromConsole();
            }
#endif

            RootCommand rootCommand = new RootCommand("A downloader for imageboards running Philomena, such as Derpibooru")
            {
                new Command("download", "Search for and download images.")
                {
                    new Option<string>(new[] { "--preset", "-p" }, "The preset to use as a base. If no preset is given, the default is used"),
                    new Option<string>(new[] { "--api-key", "-a" }, "The API key to use"),
                }.WithSearchQueryArgs().WithHandler(nameof(DownloadCommand)),

                new Command("preset", "Manage presets")
                {
                    new Command("list", "List presets").WithHandler(nameof(PresetListCommand)),

                    new Command("add", "Add a new preset")
                    {
                        new Argument<string>("name", "The name of the new preset"),
                    }.WithSearchQueryArgs().WithHandler(nameof(PresetAddCommand)),

                    new Command("delete", "Delete a preset")
                    {
                        new Argument<string>("name", "The preset to delete")
                    }.WithHandler(nameof(PresetRemoveCommand)),

                    new Command("rename", "Rename a preset.")
                    {
                        new Argument<string>("from", "The preset to rename"),
                        new Argument<string>("to", "The new name of the preset")
                    }.WithHandler(nameof(PresetRenameCommand)),

                    new Command("copy", "Copy a preset.")
                    {
                        new Argument<string>("from", "The preset to copy from"),
                        new Argument<string>("to", "The preset to copy to")
                    }.WithHandler(nameof(PresetCopyCommand)),

                    new Command("update", "Update a preset. Only given options are modified")
                    {
                        new Argument<string>("name", "The preset to update")
                    }.WithSearchQueryArgs().WithHandler(nameof(PresetUpdateCommand))
                }
            };

            await rootCommand.InvokeAsync(args);
        }

        private static async Task DownloadCommand(DownloadArgs args)
        {
            SearchConfig? baseConfig = null;
            if (args.Preset is not null)
            {
                // TODO: load preset config from settings
            }

            SearchConfig config = args.GetSearchConfig(baseConfig);
        }

        private static void PresetListCommand()
        {
            List<SearchPreset> presets = configAccess.GetPresets();
            IEnumerable<string> presetNames = presets.Select(p => p.Name);
            string presetList = string.Join(Environment.NewLine, presetNames);

            if (string.IsNullOrEmpty(presetList))
            {
                Console.WriteLine("There are no presets");
            }
            else
            {
                Console.WriteLine(presetList);
            }
        }

        private static void PresetAddCommand(PresetAddCommandArgs args)
        {
            SearchPreset? existingPreset = configAccess.GetPreset(args.Name);
            if (existingPreset is not null)
            {
                Console.WriteLine($"Preset '{args.Name}' already exists");
                return;
            }

            SearchConfig config = args.GetSearchConfig();
            SearchPreset preset = new SearchPreset(args.Name, config);
            configAccess.UpsertPreset(preset);

            Console.WriteLine($"Added preset '{args.Name}");
        }

        private static void PresetRemoveCommand(PresetRemoveCommandArgs args)
        {
            SearchPreset? preset = configAccess.GetPreset(args.Name);

            if (preset is null)
            {
                Console.WriteLine($"Preset '{args.Name}' does not exist");
                return;
            }

            configAccess.DeletePreset(preset.Id);

            Console.WriteLine($"Deleted preset '{args.Name}'");
        }

        private static void PresetRenameCommand(PresetRenameCommandArgs args)
        {
            SearchPreset? presetFrom = configAccess.GetPreset(args.From);

            if (presetFrom is null)
            {
                Console.WriteLine($"Preset '{args.From}' does not exist");
                return;
            }

            SearchPreset? presetTo = configAccess.GetPreset(args.To);

            if (presetTo is not null)
            {
                Console.WriteLine($"Preset '{args.To}' already exists");
                return;
            }

            // Update the preset's name
            presetFrom.Name = args.To;
            configAccess.UpsertPreset(presetFrom);
        }

        private static void PresetCopyCommand(PresetCopyCommandArgs args)
        {
            SearchPreset? presetFrom = configAccess.GetPreset(args.From);

            if (presetFrom is null)
            {
                Console.WriteLine($"Preset '{args.From}' does not exist");
                return;
            }

            SearchPreset? presetTo = configAccess.GetPreset(args.To);

            if (presetTo is not null)
            {
                Console.WriteLine($"Preset '{args.To}' already exists");
                return;
            }

            // Create a new preset as a copy
            presetTo = new SearchPreset(args.To, presetFrom.Config);
            configAccess.UpsertPreset(presetTo);
        }

        private static void PresetUpdateCommand(PresetUpdateCommandArgs args)
        {
            SearchPreset? preset = configAccess.GetPreset(args.Name);
            if (preset is null)
            {
                Console.WriteLine($"Preset '{args.Name}' does not exists");
                return;
            }

            // Create a new config based on the preset's config
            SearchConfig config = args.GetSearchConfig(baseConfig: preset.Config);
            preset.Config = config;
            configAccess.UpsertPreset(preset);

            Console.WriteLine($"Updated preset '{args.Name}");
        }
    }
}
