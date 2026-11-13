// File: \Services\IPhotoatomicDataService.cs
using Panex.Models;
using System.Collections.Generic;

namespace Panex.Services;

public interface IPhotoatomicDataService
{
    void LoadCrossSections(IEnumerable<Element> elements, string rootPath);
}