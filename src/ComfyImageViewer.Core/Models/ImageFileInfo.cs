namespace ComfyImageViewer.Core.Models;

/// <summary>Одно поле дополнительной (не-ComfyUI) metadata изображения.</summary>
public sealed record MetadataField(string Label, string Value, bool HiddenByDefault = false);

/// <summary>
/// GPS-позиция, извлечённая из EXIF GPS IFD (0x8825): градусы со знаком
/// (S/W → отрицательные). Создаётся только из полностью валидных DMS+reference;
/// отсутствие/битые/пустые данные — null (см. <c>ExifGps.TryParse</c>).
/// </summary>
public sealed record GpsLocationInfo(double Latitude, double Longitude);

/// <summary>
/// Базовая информация об изображении: размеры в пикселях и дополнительные поля.
/// <paramref name="DateTimeOriginal"/> — сырая ASCII-строка EXIF DateTimeOriginal (0x9003)
/// или null; используется панелью деталей как приоритетная дата съёмки (тот же тег,
/// что и в date-search). Только 0x9003 — 0x0132 сюда НЕ попадает.
/// <paramref name="Gps"/> — место съёмки из EXIF GPS IFD или null (пустой/отсутствующий
/// GPS IFD, в т. ч. privacy-заглушки со count=0 — это null).
/// </summary>
public sealed record ImageFileInfo(
    int PixelWidth,
    int PixelHeight,
    IReadOnlyList<MetadataField> Fields,
    string? DateTimeOriginal = null,
    GpsLocationInfo? Gps = null);
