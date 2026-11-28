using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace FragSplit;

public interface IImageStore
{
    Task Put(string key, byte[] bytes);
    Task<Stream> Open(string key);
}

public sealed class LocalImages : IImageStore
{
    readonly string root = Path.GetFullPath("uploads");

    public async Task Put(string key, byte[] bytes)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, key), bytes);
    }

    public Task<Stream> Open(string key) =>
        Task.FromResult<Stream>(File.OpenRead(Path.Combine(root, key)));
}

public sealed class BucketImages(IConfiguration config) : IImageStore, IDisposable
{
    readonly AmazonS3Client client = string.IsNullOrWhiteSpace(config["S3_ENDPOINT"])
        ? new()
        : new(new AmazonS3Config { ServiceURL = config["S3_ENDPOINT"], ForcePathStyle = true });

    public async Task Put(string key, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = config["S3_BUCKET"],
                Key = key,
                InputStream = stream,
                ContentType = "image/webp",
            }
        );
    }

    public async Task<Stream> Open(string key)
    {
        using var obj = await client.GetObjectAsync(config["S3_BUCKET"], key);
        var copy = new MemoryStream();
        await obj.ResponseStream.CopyToAsync(copy);
        copy.Position = 0;
        return copy;
    }

    public void Dispose() => client.Dispose();
}

public static class Media
{
    public static void Map(RouteGroupBuilder api, RouteGroupBuilder admin)
    {
        api.MapGet("/media/{key}", async (string key, IImageStore store, ShopDb db) =>
        {
            if (
                !Guid.TryParseExact(Path.GetFileNameWithoutExtension(key), "N", out _)
                || key.Length != 37
                || !key.EndsWith(".webp")
                || !await db.Images.AnyAsync(i => i.Path == "/api/v1/media/" + key)
            )
                return Results.NotFound();
            return Results.Stream(await store.Open(key), "image/webp");
        });
        admin.MapPost("/perfumes/{id:int}/images", Upload);
        admin.MapDelete("/perfumes/{id:int}/images/{imageId:int}", Remove);
        admin.MapPut(
            "/perfumes/{id:int}/images",
            async (int id, ImageEdit[] images, ShopDb db, HttpContext ctx) =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var p = await Catalog.Lock(db, id);
                await db.Entry(p).Collection(x => x.Images).LoadAsync();
                if (
                    images.Length > 16
                    || images.Select(i => i.Id).Distinct().Count() != images.Length
                    || images.Any(i => p.Images.All(old => old.Id != i.Id))
                )
                    throw new ShopError("Invalid gallery selection.");
                foreach (var old in p.Images.ToArray())
                {
                    int position = Array.FindIndex(images, x => x.Id == old.Id);
                    if (position < 0)
                        db.Remove(old);
                    else
                    {
                        old.Alt = Catalog.Field(images[position].Alt, 200, true);
                        old.Position = position;
                    }
                }
                Catalog.Log(db, Catalog.Actor(ctx), $"Update gallery {id}");
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.NoContent();
            }
        );
    }

    static async Task<IResult> Remove(int id, int imageId, ShopDb db, HttpContext ctx)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var product = await Catalog.Lock(db, id);
        await db.Entry(product).Collection(x => x.Images).LoadAsync();
        var image = product.Images.SingleOrDefault(x => x.Id == imageId);
        if (image == null)
            return Results.NoContent();

        db.Images.Remove(image);
        var position = 0;
        foreach (
            var remaining in product
                .Images.Where(x => x.Id != imageId)
                .OrderBy(x => x.Position)
                .ThenBy(x => x.Id)
        )
            remaining.Position = position++;
        Catalog.Log(db, Catalog.Actor(ctx), $"Remove image {imageId} from gallery {id}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.NoContent();
    }

    static async Task<IResult> Upload(
        int id,
        HttpRequest req,
        ShopDb db,
        IImageStore store,
        HttpContext ctx
    )
    {
        if (!req.HasFormContentType || req.ContentLength > 6 * 1024 * 1024)
            throw new ShopError("Use a JPEG, PNG or WebP image under 5 MB.");
        var form = await req.ReadFormAsync();
        var file = form.Files.GetFile("image");
        var alt = Catalog.Field(form["alt"], 200, true);
        if (file == null || file.Length is <= 0 or > 5242880)
            throw new ShopError("Choose an image up to 5 MB.");
        using var stream = file.OpenReadStream();
        using var codec = SKCodec.Create(stream);
        var mime = codec?.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Webp => "image/webp",
            _ => "",
        };
        if (
            codec == null
            || codec.Info.Width <= 0
            || codec.Info.Height <= 0
            || mime == ""
            || mime != file.ContentType
            || (long)codec.Info.Width * codec.Info.Height > 16000000
        )
            throw new ShopError("Invalid image format or dimensions.");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new ShopError("Invalid image.");
        var scale = Math.Min(1d, 1600d / Math.Max(bitmap.Width, bitmap.Height));
        using var small = bitmap.Resize(
            new SKImageInfo(
                Math.Max(1, (int)(bitmap.Width * scale)),
                Math.Max(1, (int)(bitmap.Height * scale))
            ),
            new SKSamplingOptions(SKFilterMode.Linear)
        ) ?? throw new ShopError("Could not resize image.");
        using var encoded = small.Encode(SKEncodedImageFormat.Webp, 85)
            ?? throw new ShopError("Could not encode image.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await Catalog.Lock(db, id);
        await db.Entry(p).Collection(x => x.Images).LoadAsync();
        if (p.Images.Count >= 16)
            throw new ShopError("Gallery holds up to 16 images.");
        var key = Guid.NewGuid().ToString("N") + ".webp";
        await store.Put(key, encoded.ToArray());
        p.Images.Add(
            new ProductImage
            {
                Path = "/api/v1/media/" + key,
                Alt = alt,
                Position = p.Images.Count,
            }
        );
        Catalog.Log(db, Catalog.Actor(ctx), $"Upload image for {id}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.NoContent();
    }
}
