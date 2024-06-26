﻿using LSLib.LS.Enums;

namespace LSLib.LS;

public class ResourceLoadParameters
{
    /// <summary>
    /// Byte-swap the last 8 bytes of GUIDs when serializing to/from string
    /// </summary>
    public bool ByteSwapGuids = true;

    public static ResourceLoadParameters FromGameVersion(Game game)
    {
        var p = new ResourceLoadParameters();
        // No game-specific settings yet
        return p;
    }

    public void ToSerializationSettings(NodeSerializationSettings settings)
    {
        settings.DefaultByteSwapGuids = ByteSwapGuids;
    }
}

public class ResourceConversionParameters
{
    /// <summary>
    /// Format of generated PAK files
    /// </summary>
    public PackageVersion PAKVersion;

    /// <summary>
    /// Format of generated LSF files
    /// </summary>
    public LSFVersion LSF = LSFVersion.MaxWriteVersion;

    /// <summary>
    /// Store sibling/neighbour node data in LSF files (usually done by savegames and dictionary-like files)
    /// (null = auto-detect based on input resource file)
    /// </summary>
    public LSFMetadataFormat? MetadataFormat = null;

    /// <summary>
    /// Format of generated LSX files
    /// </summary>
    public LSXVersion LSX = LSXVersion.V4;

    /// <summary>
    /// Pretty-print (format) LSX/LSJ files
    /// </summary>
    public bool PrettyPrint = true;

    /// <summary>
    /// LSF/LSB compression method
    /// </summary>
    public CompressionMethod Compression = CompressionMethod.None;

    /// <summary>
    /// LSF/LSB compression level (i.e. size/compression time tradeoff)
    /// </summary>
    public LSCompressionLevel CompressionLevel = LSCompressionLevel.Default;

    /// <summary>
    /// Byte-swap the last 8 bytes of GUIDs when serializing to/from string
    /// </summary>
    public bool ByteSwapGuids = true;

    public static ResourceConversionParameters FromGameVersion(Game game)
    {
        return new ResourceConversionParameters
        {
            PAKVersion = game.PAKVersion(),
            LSF = game.LSFVersion(),
            LSX = game.LSXVersion()
        };
    }

    public void ToSerializationSettings(NodeSerializationSettings settings)
    {
        settings.DefaultByteSwapGuids = ByteSwapGuids;
    }

    public void ToSerializationSettings(NodeSerializationSettings settings, Resource res)
    {
        settings.DefaultByteSwapGuids = ByteSwapGuids;
        settings.LSFMetadata = res.MetadataFormat ?? settings.LSFMetadata;
    }
}

public class ResourceUtils
{
    public delegate void ProgressUpdateDelegate(string status, long numerator, long denominator);
    public ProgressUpdateDelegate progressUpdate = delegate { };
    
    public delegate void ErrorDelegate(string path, Exception e);
    public ErrorDelegate errorDelegate = delegate { };

    public static ResourceFormat ExtensionToResourceFormat(string path)
    {
        var extension = Path.GetExtension(path).ToLower();

        return extension switch
        {
            ".lsx" => ResourceFormat.LSX,
            ".lsb" => ResourceFormat.LSB,
            ".lsf" or ".lsfx" or ".lsbc" or ".lsbs" => ResourceFormat.LSF,
            ".lsj" => ResourceFormat.LSJ,
            _ => throw new ArgumentException("Unrecognized file extension: " + extension),
        };
    }

    public static Resource LoadResource(string inputPath, ResourceLoadParameters loadParams)
    {
        return LoadResource(inputPath, ExtensionToResourceFormat(inputPath), loadParams);
    }

    public static Resource LoadResource(string inputPath, ResourceFormat format, ResourceLoadParameters loadParams)
    {
        using var stream = File.Open(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return LoadResource(stream, format, loadParams);
    }

    public static Resource LoadResource(Stream stream, ResourceFormat format, ResourceLoadParameters loadParams)
    {
        switch (format)
        {
            case ResourceFormat.LSX:
                {
                    using var reader = new LSXReader(stream);
                    loadParams.ToSerializationSettings(reader.SerializationSettings);
                    return reader.Read();
                }

            case ResourceFormat.LSB:
                {
                    using var reader = new LSBReader(stream);
                    return reader.Read();
                }

            case ResourceFormat.LSF:
                {
                    using var reader = new LSFReader(stream);
                    return reader.Read();
                }

            case ResourceFormat.LSJ:
                {
                    using var reader = new LSJReader(stream);
                    loadParams.ToSerializationSettings(reader.SerializationSettings);
                    return reader.Read();
                }

            default:
                throw new ArgumentException("Invalid resource format");
        }
    }

    public static void SaveResource(Resource resource, string outputPath, ResourceConversionParameters conversionParams)
    {
        SaveResource(resource, outputPath, ExtensionToResourceFormat(outputPath), conversionParams);
    }

    public static void SaveResource(Resource resource, string outputPath, ResourceFormat format, ResourceConversionParameters conversionParams)
    {
        FileManager.TryToCreateDirectory(outputPath);

        using var file = File.Open(outputPath, FileMode.Create, FileAccess.Write);
        switch (format)
        {
            case ResourceFormat.LSX:
                {
                    var writer = new LSXWriter(file)
                    {
                        Version = conversionParams.LSX,
                        PrettyPrint = conversionParams.PrettyPrint
                    };
                    conversionParams.ToSerializationSettings(writer.SerializationSettings, resource);
                    writer.Write(resource);
                    break;
                }

            case ResourceFormat.LSB:
                {
                    var writer = new LSBWriter(file);
                    writer.Write(resource);
                    break;
                }

            case ResourceFormat.LSF:
                {
                    var writer = new LSFWriter(file)
                    {
                        Version = conversionParams.LSF,
                        MetadataFormat = conversionParams.MetadataFormat ?? resource.MetadataFormat ?? LSFMetadataFormat.None,
                        Compression = conversionParams.Compression,
                        CompressionLevel = conversionParams.CompressionLevel
                    };
                    writer.Write(resource);
                    break;
                }

            case ResourceFormat.LSJ:
                {
                    var writer = new LSJWriter(file)
                    {
                        PrettyPrint = conversionParams.PrettyPrint
                    };
                    conversionParams.ToSerializationSettings(writer.SerializationSettings, resource);
                    writer.Write(resource);
                    break;
                }

            default:
                throw new ArgumentException("Invalid resource format");
        }
    }

    private bool IsA(string path, ResourceFormat format)
    {
        var extension = Path.GetExtension(path).ToLower();
        return format switch
        {
            ResourceFormat.LSX => extension == ".lsx",
            ResourceFormat.LSB => extension == ".lsb",
            ResourceFormat.LSF => extension == ".lsf" || extension == ".lsbc" || extension == ".lsfx",
            ResourceFormat.LSJ => extension == ".lsj",
            _ => false,
        };
    }

    private void EnumerateFiles(List<string> paths, string rootPath, string currentPath, ResourceFormat format)
    {
        foreach (string filePath in Directory.GetFiles(currentPath))
        {
            if (IsA(filePath, format))
            {
                var relativePath = filePath[rootPath.Length..];
                if (relativePath[0] == '/' || relativePath[0] == '\\')
                {
                    relativePath = relativePath[1..];
                }

                paths.Add(relativePath);
            }
        }

        foreach (string directoryPath in Directory.GetDirectories(currentPath))
        {
            EnumerateFiles(paths, rootPath, directoryPath, format);
        }
    }

    public void ConvertResources(string inputDir, string outputDir, ResourceFormat inputFormat, ResourceFormat outputFormat, 
        ResourceLoadParameters loadParams, ResourceConversionParameters conversionParams)
    {
        this.progressUpdate("Enumerating files ...", 0, 1);
        var paths = new List<string>();
        EnumerateFiles(paths, inputDir, inputDir, inputFormat);

        this.progressUpdate("Converting resources ...", 0, 1);
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            var inPath = Path.Join(inputDir, path);
            var outPath = Path.Join(outputDir, Path.ChangeExtension(path, outputFormat.ToString().ToLower()));

            FileManager.TryToCreateDirectory(outPath);

            this.progressUpdate("Converting: " + inPath, i, paths.Count);
            try
            {
                var resource = LoadResource(inPath, inputFormat, loadParams);
                SaveResource(resource, outPath, outputFormat, conversionParams);
            }
            catch (Exception ex)
            {
                errorDelegate(inPath, ex);
            }
        }
    }
}
