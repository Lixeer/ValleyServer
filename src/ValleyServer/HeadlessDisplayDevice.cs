#pragma warning disable SYSLIB0050

using xTile;
using xTile.Display;
using xTile.Dimensions;
using xTile.Tiles;

namespace HeadlessServer
{
    /// <summary>
    /// xTile display backend for the dedicated server. Map loading updates seasonal
    /// tilesheets by disposing/loading them through Game1.mapDisplayDevice even though
    /// no rendering is performed. A non-null no-op implementation keeps that lifecycle
    /// safe without creating an XNA GraphicsDevice (which is unavailable headlessly).
    /// </summary>
    internal sealed class HeadlessDisplayDevice : IDisplayDevice
    {
        public void LoadTileSheet(TileSheet tileSheet) { }
        public void DisposeTileSheet(TileSheet tileSheet) { }
        public void BeginScene(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch) { }
        public void SetClippingRegion(Rectangle clippingRegion) { }
        public void DrawTile(Tile tile, Location location, float layerDepth) { }
        public void EndScene() { }
    }
}
