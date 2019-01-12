using System;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

namespace InterlockingTilesEffect
{
    public class PluginSupportInfo : IPluginSupportInfo
    {
        public string Author => base.GetType().Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>().Copyright;
        public string Copyright => base.GetType().Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>().Description;
        public string DisplayName => base.GetType().Assembly.GetCustomAttribute<AssemblyProductAttribute>().Product;
        public Version Version => base.GetType().Assembly.GetName().Version;
        public Uri WebsiteUri => new Uri("https://forums.getpaint.net/index.php?showtopic=113640");
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "Interlocking Tiles")]
    public class InterlockingTiles : PropertyBasedEffect
    {
        private int horMargin = 0;
        private int verMargin = 0;
        private Pair<double, double> position = Pair.Create(0.0, 0.0);

        private Surface trimmedSurface;
        private readonly BinaryPixelOp normalOp = LayerBlendModeUtil.CreateCompositionOp(LayerBlendMode.Normal);

        private readonly static Image StaticIcon = new Bitmap(typeof(InterlockingTiles), "InterlockingTiles.png");

        public InterlockingTiles()
            : base("Interlocking Tiles", StaticIcon, "Fill", new EffectOptions { Flags = EffectFlags.Configurable })
        {
        }

        private enum PropertyNames
        {
            HorMargin,
            VerMargin,
            LinkedMargins,
            Position,
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>
            {
                new Int32Property(PropertyNames.HorMargin, 0, 0, 1000),
                new Int32Property(PropertyNames.VerMargin, 0, 0, 1000),
                new BooleanProperty(PropertyNames.LinkedMargins, true),
                new DoubleVectorProperty(PropertyNames.Position, Pair.Create(0.0, 0.0), Pair.Create(-1.0, -1.0), Pair.Create(+1.0, +1.0)),
            };

            List<PropertyCollectionRule> propRules = new List<PropertyCollectionRule>
            {
                new LinkValuesBasedOnBooleanRule<int, Int32Property>(new object[] { PropertyNames.HorMargin, PropertyNames.VerMargin }, PropertyNames.LinkedMargins, false)
            };

            return new PropertyCollection(props, propRules);
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.HorMargin, ControlInfoPropertyNames.DisplayName, "Horizontal Negative Margin");
            configUI.SetPropertyControlValue(PropertyNames.VerMargin, ControlInfoPropertyNames.DisplayName, "Vertical Negative Margin");
            configUI.SetPropertyControlValue(PropertyNames.LinkedMargins, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlValue(PropertyNames.LinkedMargins, ControlInfoPropertyNames.Description, "Link Margins");
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.DisplayName, "Position");
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.SliderSmallChangeX, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.SliderLargeChangeX, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.UpDownIncrementX, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.SliderSmallChangeY, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.SliderLargeChangeY, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.UpDownIncrementY, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.DecimalPlaces, 3);
            Rectangle selection3 = this.EnvironmentParameters.GetSelection(this.EnvironmentParameters.SourceSurface.Bounds).GetBoundsInt();
            ImageResource imageResource3 = ImageResource.FromImage(this.EnvironmentParameters.SourceSurface.CreateAliasedBitmap(selection3));
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.StaticImageUnderlay, imageResource3);

            return configUI;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            this.horMargin = newToken.GetProperty<Int32Property>(PropertyNames.HorMargin).Value;
            this.verMargin = newToken.GetProperty<Int32Property>(PropertyNames.VerMargin).Value;
            this.position = newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Position).Value;

            Rectangle selection = this.EnvironmentParameters.GetSelection(srcArgs.Surface.Bounds).GetBoundsInt();

            if (this.trimmedSurface is null)
            {
                Rectangle trimmedBounds = GetTrimmedBounds(srcArgs.Surface, selection);

                this.trimmedSurface = new Surface(trimmedBounds.Size);
                this.trimmedSurface.CopySurface(srcArgs.Surface, Point.Empty, trimmedBounds);
            }

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        protected override void OnRender(Rectangle[] renderRects, int startIndex, int length)
        {
            if (length == 0) return;

            for (int i = startIndex; i < startIndex + length; ++i)
            {
                Render(this.DstArgs.Surface, this.SrcArgs.Surface, renderRects[i]);
            }
        }

        private void Render(Surface dst, Surface src, Rectangle rect)
        {
            Rectangle selection = this.EnvironmentParameters.GetSelection(src.Bounds).GetBoundsInt();

            Size margin = new Size
            {
                Width = Math.Min(this.horMargin, this.trimmedSurface.Width / 2),
                Height = Math.Min(this.verMargin, this.trimmedSurface.Height / 2)
            };

            Size effectiveTileSize = new Size(this.trimmedSurface.Width - margin.Width, this.trimmedSurface.Height - margin.Height);

            int maxHorLoops = (selection.Width - margin.Width) / effectiveTileSize.Width;
            int maxVerLoops = (selection.Height - margin.Height) / effectiveTileSize.Height;

            Size sheetSize = new Size
            {
                Width = maxHorLoops * effectiveTileSize.Width + margin.Width,
                Height = maxVerLoops * effectiveTileSize.Height + margin.Height
            };

            PointF offsetForCenter = new PointF((selection.Width - sheetSize.Width) / 2f, (selection.Height - sheetSize.Height) / 2f);

            Rectangle sheetRect = new Rectangle
            {
                X = (int)Math.Round(selection.X + offsetForCenter.X + (this.position.First * offsetForCenter.X)),
                Y = (int)Math.Round(selection.Y + offsetForCenter.Y + (this.position.Second * offsetForCenter.Y)),
                Size = sheetSize
            };

            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                if (this.IsCancelRequested) return;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    if (!sheetRect.Contains(x, y))
                    {
                        dst[x, y] = ColorBgra.Transparent;
                        continue;
                    }

                    int xLoop = (x - sheetRect.X) / effectiveTileSize.Width;
                    int xOffset = xLoop * margin.Width + x - sheetRect.X;
                    int xOverlapStart = effectiveTileSize.Width * xLoop + sheetRect.X;

                    int yLoop = (y - sheetRect.Y) / effectiveTileSize.Height;
                    int yOffset = yLoop * margin.Height + y - sheetRect.Y;
                    int yOverlapStart = effectiveTileSize.Height * yLoop + sheetRect.Y;

                    if (xLoop > 0 && yLoop > 0 &&
                        x >= xOverlapStart && x < xOverlapStart + margin.Width &&
                        y >= yOverlapStart && y < yOverlapStart + margin.Height)
                    {
                        if (xLoop < maxHorLoops && yLoop < maxVerLoops)
                        {
                            ColorBgra colorA = this.normalOp.Apply(
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset - margin.Width, yOffset),
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset, yOffset));
                            ColorBgra colorB = this.normalOp.Apply(
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset, yOffset - margin.Height),
                                colorA);
                            dst[x, y] = this.normalOp.Apply(
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset - margin.Width, yOffset - margin.Height),
                                colorB);
                        }
                        else if (xLoop < maxHorLoops)
                        {
                            dst[x, y] = this.normalOp.Apply(
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset - margin.Width, yOffset - margin.Height),
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset, yOffset - margin.Height));
                        }
                        else if (yLoop < maxVerLoops)
                        {
                            dst[x, y] = this.normalOp.Apply(
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset - margin.Width, yOffset - margin.Height),
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset - margin.Width, yOffset));
                        }
                        else
                        {
                            dst[x, y] = this.trimmedSurface.GetBilinearSampleWrapped(xOffset - margin.Width, yOffset - margin.Height);
                        }
                    }
                    else if (xLoop > 0 && x >= xOverlapStart && x < xOverlapStart + margin.Width)
                    {
                        dst[x, y] = (xLoop < maxHorLoops) ?
                            this.normalOp.Apply(
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset - margin.Width, yOffset),
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset, yOffset)) :
                            this.trimmedSurface.GetBilinearSampleWrapped(xOffset - margin.Width, yOffset);
                    }
                    else if (yLoop > 0 && y >= yOverlapStart && y < yOverlapStart + margin.Height)
                    {
                        dst[x, y] = (yLoop < maxVerLoops) ?
                            this.normalOp.Apply(
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset, yOffset - margin.Height),
                                this.trimmedSurface.GetBilinearSampleWrapped(xOffset, yOffset)) :
                            this.trimmedSurface.GetBilinearSampleWrapped(xOffset, yOffset - margin.Height);
                    }
                    else
                    {
                        dst[x, y] = this.trimmedSurface.GetBilinearSampleWrapped(xOffset, yOffset);
                    }
                }
            }
        }

        private static Rectangle GetTrimmedBounds(Surface srcSurface, Rectangle srcBounds)
        {
            int xMin = int.MaxValue,
                xMax = int.MinValue,
                yMin = int.MaxValue,
                yMax = int.MinValue;

            bool foundPixel = false;

            // Find xMin
            for (int x = srcBounds.Left; x < srcBounds.Right; x++)
            {
                bool stop = false;
                for (int y = srcBounds.Top; y < srcBounds.Bottom; y++)
                {
                    if (srcSurface[x, y].A != 0)
                    {
                        xMin = x;
                        stop = true;
                        foundPixel = true;
                        break;
                    }
                }
                if (stop)
                {
                    break;
                }
            }

            // Image is empty...
            if (!foundPixel)
            {
                return srcBounds;
            }

            // Find yMin
            for (int y = srcBounds.Top; y < srcBounds.Bottom; y++)
            {
                bool stop = false;
                for (int x = xMin; x < srcBounds.Right; x++)
                {
                    if (srcSurface[x, y].A != 0)
                    {
                        yMin = y;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                {
                    break;
                }
            }

            // Find xMax
            for (int x = srcBounds.Right - 1; x >= xMin; x--)
            {
                bool stop = false;
                for (int y = yMin; y < srcBounds.Bottom; y++)
                {
                    if (srcSurface[x, y].A != 0)
                    {
                        xMax = x;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                {
                    break;
                }
            }

            // Find yMax
            for (int y = srcBounds.Bottom - 1; y >= yMin; y--)
            {
                bool stop = false;
                for (int x = xMin; x <= xMax; x++)
                {
                    if (srcSurface[x, y].A != 0)
                    {
                        yMax = y;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                {
                    break;
                }
            }

            return Rectangle.FromLTRB(xMin, yMin, xMax + 1, yMax + 1);
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                this.trimmedSurface?.Dispose();
            }

            base.OnDispose(disposing);
        }
    }
}
