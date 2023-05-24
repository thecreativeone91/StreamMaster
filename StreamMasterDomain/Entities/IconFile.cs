﻿namespace StreamMasterDomain.Entities;

public class IconFile : CacheEntity
{
    public IconFile()
    {
        //DirectoryLocation = FileDefinitions.Icon.DirectoryLocation;
        FileExtension = FileDefinitions.Icon.FileExtension;
        SMFileType = FileDefinitions.Icon.SMFileType;
    }
}
