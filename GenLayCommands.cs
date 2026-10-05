using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

// 'Exception' es ambigua entre Autodesk.AutoCAD.Runtime.Exception y System.Exception:
// en todo el archivo se califica como System.Exception.

[assembly: CommandClass(typeof(AGRDB.GenLay.GenLayCommands))]

namespace AGRDB.GenLay
{
    public class GenLayCommands
    {
        // ------------------------------------------------------------------
        // COMANDO: solo abre el dialogo y ejecuta lo que este devuelva.
        // ------------------------------------------------------------------
        [CommandMethod("GENLAY", CommandFlags.Modal)]
        public void GenLay()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;

            try
            {
                using (GenLayForm form = new GenLayForm())
                {
                    // ShowModalDialog de AutoCAD, no form.ShowDialog():
                    // AutoCAD debe controlar su propio bucle de mensajes.
                    if (AcadApp.ShowModalDialog(form) != WinForms.DialogResult.OK)
                    {
                        ed.WriteMessage("\nGENLAY cancelado.");
                        return;
                    }

                    List<FrameInfo> frames = new List<FrameInfo>(form.Frames);
                    AcadIo.SortFrames(frames, form.FrameOrder);

                    int created = CreateLayouts(doc,
                                                form.BaseLayout,
                                                frames,
                                                form.ScaleDenominator,
                                                form.PaperPerModel,
                                                form.Prefix,
                                                form.StartNumber,
                                                form.ResizeViewport);

                    ed.WriteMessage("\nGENLAY: {0} de {1} hojas generadas a escala 1:{2}.",
                                    created, frames.Count, form.ScaleDenominator);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nGENLAY - Error: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // CREACION DE LAYOUTS
        // ------------------------------------------------------------------
        private static int CreateLayouts(Document doc, string baseLayout, List<FrameInfo> frames,
                                         double denom, double paperPerModel,
                                         string prefix, int startNumber, bool resizeVp)
        {
            Database db = doc.Database;
            Editor ed = doc.Editor;
            LayoutManager lm = LayoutManager.Current;

            string originalLayout = lm.CurrentLayout;
            int tabOrder = AcadIo.GetLayoutNames().Count + 1;
            int created = 0;
            string firstCreated = null;

            for (int i = 0; i < frames.Count; i++)
            {
                FrameInfo f = frames[i];
                string wanted = UniqueLayoutName(AcadIo.BuildSheetName(prefix, startNumber + i));

                string realName = wanted;
                try
                {
                    lm.CloneLayout(baseLayout, wanted, tabOrder + i);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\n  [{0}] no se pudo clonar: {1}", wanted, ex.Message);
                    continue;
                }

                // El layout debe estar activo: si no, el viewport clonado conserva
                // Number = 0 y no se puede encender.
                lm.CurrentLayout = realName;
                AcadApp.SetSystemVariable("TILEMODE", 0);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Layout lay = (Layout)tr.GetObject(lm.GetLayoutId(realName), OpenMode.ForRead);
                    Viewport vp = GetMainViewport(tr, lay);

                    if (vp == null)
                    {
                        ed.WriteMessage("\n  [{0}] el layout base no tiene viewport flotante.", realName);
                        tr.Commit();
                        continue;
                    }

                    vp.UpgradeOpen();

                    if (vp.Locked) vp.Locked = false;
                    vp.On = true;

                    vp.ViewDirection = Vector3d.ZAxis;
                    vp.ViewTarget = Point3d.Origin;
                    vp.TwistAngle = 0.0;

                    if (resizeVp)
                    {
                        vp.Width = f.Width * paperPerModel / denom;
                        vp.Height = f.Height * paperPerModel / denom;
                    }

                    vp.ViewHeight = vp.Height * denom / paperPerModel;
                    vp.ViewCenter = new Point2d(f.Center.X, f.Center.Y);

                    vp.Locked = true;

                    tr.Commit();
                }

                if (firstCreated == null) firstCreated = realName;
                created++;
                ed.WriteMessage("\n  [{0}] OK", realName);
            }

            try { lm.CurrentLayout = firstCreated ?? originalLayout; }
            catch (System.Exception) { }

            ed.Regen();
            return created;
        }

        private static Viewport GetMainViewport(Transaction tr, Layout lay)
        {
            Viewport best = null;
            foreach (ObjectId id in lay.GetViewports())
            {
                Viewport vp = tr.GetObject(id, OpenMode.ForRead) as Viewport;
                if (vp == null || vp.Number == 1) continue;
                if (best == null || (vp.Width * vp.Height) > (best.Width * best.Height))
                    best = vp;
            }
            return best;
        }

        private static string UniqueLayoutName(string baseName)
        {
            HashSet<string> existing = new HashSet<string>(
                AcadIo.GetLayoutNames(), StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(baseName)) return baseName;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + "_" + i;
                if (!existing.Contains(candidate)) return candidate;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}