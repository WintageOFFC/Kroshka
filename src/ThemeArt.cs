using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Kroshka;

// Shared, immutable display-size artwork. Theme changes only switch references.
// No decoding, tinting or bitmap allocation in the animation and paint callbacks.
internal static class ThemeArt
{
    static readonly string[] Columns = ["Mascot","Loading","MenuLocation","MenuRecycle","Themes"];
    static readonly Dictionary<(string Style,string Name),Bitmap> pictures = new();
    static readonly Dictionary<string,Icon> icons = new();
    static readonly Lazy<Bitmap> sheet = new(() => Decode("ThemeArt"));

    static Bitmap Decode(string name)
    {
        using var stream=typeof(ThemeArt).Assembly.GetManifestResourceStream("Kroshka."+name)
            ?? throw new InvalidOperationException("Missing artwork: "+name);
        using var image=Image.FromStream(stream);
        return new Bitmap(image);
    }

    public static Bitmap Picture(string name,string? style=null)
    {
        style ??= Theme.Style;
        if(pictures.TryGetValue((style,name),out var cached))return cached;
        int size=name switch { "Mascot" or "Themes"=>144,"Loading"=>64,_=>48 };
        Bitmap source; Rectangle crop;
        if(style=="Classic") {
            source=Decode(name);
            crop=new Rectangle(0,0,name=="Themes"?source.Width/3:source.Width,source.Height);
        } else {
            source=sheet.Value;
            int column=Array.IndexOf(Columns,name);
            if(column<0)throw new ArgumentException("Unknown artwork",nameof(name));
            int w=source.Width/5,h=source.Height/2;
            crop=new Rectangle(column*w,style=="Vintage"?0:h,w,h);
        }
        try {
            var picture=new Bitmap(size,size,PixelFormat.Format32bppPArgb);
            using var g=Graphics.FromImage(picture);
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(source,new Rectangle(0,0,size,size),crop,GraphicsUnit.Pixel);
            pictures.Add((style,name),picture);
            return picture;
        } finally {if(style=="Classic")source.Dispose();}
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    public static Icon WindowIcon()
    {
        string style=Theme.Style;
        if(icons.TryGetValue(style,out var icon))return icon;
        if(style=="Classic") {
            using var stream=typeof(ThemeArt).Assembly.GetManifestResourceStream("Kroshka.AppIcon")!;
            using var original=new Icon(stream);
            icon=(Icon)original.Clone();
        } else {
            using var bitmap=new Bitmap(Picture("Mascot"),new Size(48,48));
            var h=bitmap.GetHicon();
            try{icon=(Icon)Icon.FromHandle(h).Clone();}finally{DestroyIcon(h);}
        }
        icons.Add(style,icon);return icon;
    }
}
