using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AGRDB.GenLay
{
    /// <summary>Marco (polilinea cerrada) leido del modelo.</summary>
    public class FrameInfo
    {
        public ObjectId Id;
        public Point3d Center;
        public double Width;
        public double Height;
        public string Layer;
    }

    /// <summary>Capa del dibujo, con lo necesario para pintarla en el combo.</summary>
    public class LayerInfo
    {
        public string Name;
        public System.Drawing.Color Color;
        public bool Usable;      // ni apagada ni congelada
        public bool IsAll;       // item "(Todas las capas)"

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Criterio para ordenar los marcos antes de generar las hojas.
    /// </summary>
    public enum FrameOrder
    {
        /// <summary>Conserva el orden en que el usuario selecciono los marcos.</summary>
        UserPick,

        /// <summary>Por columnas: avanza en X. Columnas de izquierda a derecha; dentro de cada columna, de arriba a abajo.</summary>
        ByColumns,

        /// <summary>Por filas: avanza en Y. Filas de arriba a abajo; dentro de cada fila, de izquierda a derecha.</summary>
        ByRows
    }

    /// <summary>
    /// Toda la conversacion con AutoCAD. El formulario llama aqui;
    /// asi la UI no queda mezclada con transacciones.
    /// </summary>
    public static class AcadIo
    {
        public const string AllLayersLabel = "(Todas las capas)";

        // ------------------------------------------------------------------
        // NOMBRES DE HOJA  (fuente unica de verdad)
        // ------------------------------------------------------------------
        /// <summary>
        /// Formato de relleno de ceros para el numero de hoja.
        /// "D2" -> 01, 02 ...   "D3" -> 001, 002 ...   "D4" -> 0001 ...
        /// Cambia la cantidad de digitos aqui, en un solo lugar.
        /// </summary>
        public const string NumberFormat = "D2";

        /// <summary>
        /// Prefijo por defecto que ofrece el dialogo. Puede ser numero o texto.
        /// </summary>
        public const string DefaultPrefix = "";

        /// <summary>
        /// Arma el nombre de una hoja: prefijo (numero o texto) + numero
        /// rellenado segun NumberFormat. La vista previa del formulario y la
        /// creacion real de layouts DEBEN usar este metodo, para que
        /// "lo que se ve" sea siempre "lo que se crea".
        /// </summary>
        public static string BuildSheetName(string prefix, int number)
        {
            return (prefix ?? string.Empty) + number.ToString(NumberFormat);
        }

        // ------------------------------------------------------------------
        // CAPAS
        // ------------------------------------------------------------------
        public static List<LayerInfo> GetLayers()
        {
            List<LayerInfo> result = new List<LayerInfo>();
            result.Add(new LayerInfo
            {
                Name = AllLayersLabel,
                Color = System.Drawing.Color.Transparent,
                Usable = true,
                IsAll = true
            });

            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return result;

            List<LayerInfo> layers = new List<LayerInfo>();

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);

                foreach (ObjectId id in lt)
                {
                    LayerTableRecord ltr = tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                    if (ltr == null) continue;

                    System.Drawing.Color color = System.Drawing.Color.LightGray;
                    try { color = ltr.Color.ColorValue; }
                    catch (System.Exception) { }

                    layers.Add(new LayerInfo
                    {
                        Name = ltr.Name,
                        Color = color,
                        Usable = !ltr.IsOff && !ltr.IsFrozen,
                        IsAll = false
                    });
                }
                tr.Commit();
            }

            layers.Sort(delegate (LayerInfo a, LayerInfo b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            result.AddRange(layers);
            return result;
        }

        // ------------------------------------------------------------------
        // LAYOUTS
        // ------------------------------------------------------------------
        public static List<string> GetLayoutNames()
        {
            List<string> names = new List<string>();

            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return names;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                DBDictionary dict = (DBDictionary)tr.GetObject(
                    doc.Database.LayoutDictionaryId, OpenMode.ForRead);

                foreach (DBDictionaryEntry e in dict)
                {
                    if (!e.Key.Equals("Model", StringComparison.OrdinalIgnoreCase))
                        names.Add(e.Key);
                }
                tr.Commit();
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static string GetCurrentLayoutName()
        {
            string name = LayoutManager.Current.CurrentLayout;
            if (string.IsNullOrEmpty(name) ||
                name.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return name;
        }

        // ------------------------------------------------------------------
        // SELECCION DE MARCOS
        // ------------------------------------------------------------------
        /// <summary>
        /// Pide al usuario los marcos en el editor, filtrando por capa.
        /// Devuelve null si el usuario cancela con ESC.
        /// Los marcos se devuelven en el orden en que AutoCAD los agrega a la
        /// seleccion; al elegirlos uno por uno, ese es el orden de clic del
        /// usuario (util para FrameOrder.UserPick).
        /// </summary>
        /// <param name="layerName">Nombre de capa, o null para no filtrar.</param>
        /// <param name="skipped">Objetos descartados por no ser cerrados o no tener area.</param>
        public static List<FrameInfo> PickFrames(string layerName, out int skipped)
        {
            skipped = 0;

            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;

            Editor ed = doc.Editor;

            // Devolver el foco a AutoCAD: el dialogo lo tiene tomado.
            AcadApp.MainWindow.Focus();

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = string.IsNullOrEmpty(layerName)
                ? "\nSeleccione las polilineas de los marcos"
                : "\nSeleccione las polilineas en la capa " + layerName;

            PromptSelectionResult psr;
            using (doc.LockDocument())
            {
                psr = ed.GetSelection(pso, BuildFilter(layerName));
            }

            if (psr.Status != PromptStatus.OK) return null;

            List<FrameInfo> frames = new List<FrameInfo>();

            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;

                    Curve curve = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Curve;
                    if (curve == null || !curve.Closed) { skipped++; continue; }

                    Extents3d ext;
                    try { ext = curve.GeometricExtents; }
                    catch (System.Exception) { skipped++; continue; }

                    double w = ext.MaxPoint.X - ext.MinPoint.X;
                    double h = ext.MaxPoint.Y - ext.MinPoint.Y;

                    if (w <= Tolerance.Global.EqualPoint || h <= Tolerance.Global.EqualPoint)
                    {
                        skipped++;
                        continue;
                    }

                    frames.Add(new FrameInfo
                    {
                        Id = so.ObjectId,
                        Center = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                                             (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                                             0.0),
                        Width = w,
                        Height = h,
                        Layer = curve.Layer
                    });
                }
                tr.Commit();
            }

            return frames;
        }

        private static SelectionFilter BuildFilter(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Start, "POLYLINE"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                });
            }

            return new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<AND"),
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>"),
                new TypedValue((int)DxfCode.LayerName, layerName),
                new TypedValue((int)DxfCode.Operator, "AND>")
            });
        }

        // ------------------------------------------------------------------
        // ORDENAMIENTO DE MARCOS
        // ------------------------------------------------------------------
        /// <summary>
        /// Ordena la lista de marcos in situ segun el criterio indicado.
        /// - UserPick    : no reordena (respeta el orden de seleccion).
        /// - ByColumns : avanza en X (columnas de izquierda a derecha; dentro de cada columna, de arriba a abajo).
        /// - ByRows : avanza en Y (filas de arriba a abajo; dentro de cada fila, de izquierda a derecha).
        /// El agrupamiento por filas/columnas usa una tolerancia derivada del
        /// tamano promedio de los marcos, de modo que marcos casi alineados
        /// caen en la misma fila/columna aunque sus centros no coincidan exacto.
        /// </summary>
        public static void SortFrames(List<FrameInfo> frames, FrameOrder order)
        {
            if (frames == null || frames.Count < 2) return;

            switch (order)
            {
                case FrameOrder.UserPick:
                    return; // se conserva el orden de seleccion

                case FrameOrder.ByColumns:
                    SortByColumns(frames);   // avance en X
                    break;

                case FrameOrder.ByRows:
                    SortByRows(frames);      // avance en Y
                    break;
            }
        }

        /// <summary>Filas: arriba -> abajo; dentro de cada fila izquierda -> derecha.</summary>
        private static void SortByRows(List<FrameInfo> frames)
        {
            double tol = frames.Average(f => f.Height) * 0.5;

            List<List<FrameInfo>> rows = new List<List<FrameInfo>>();
            foreach (FrameInfo f in frames.OrderByDescending(x => x.Center.Y))
            {
                List<FrameInfo> row = rows.FirstOrDefault(
                    r => Math.Abs(r[0].Center.Y - f.Center.Y) <= tol);

                if (row == null)
                {
                    row = new List<FrameInfo>();
                    rows.Add(row);
                }
                row.Add(f);
            }

            frames.Clear();
            foreach (List<FrameInfo> row in rows)
                frames.AddRange(row.OrderBy(f => f.Center.X));
        }

        /// <summary>Columnas: izquierda -> derecha; dentro de cada columna arriba -> abajo.</summary>
        private static void SortByColumns(List<FrameInfo> frames)
        {
            double tol = frames.Average(f => f.Width) * 0.5;

            List<List<FrameInfo>> cols = new List<List<FrameInfo>>();
            foreach (FrameInfo f in frames.OrderBy(x => x.Center.X))
            {
                List<FrameInfo> col = cols.FirstOrDefault(
                    c => Math.Abs(c[0].Center.X - f.Center.X) <= tol);

                if (col == null)
                {
                    col = new List<FrameInfo>();
                    cols.Add(col);
                }
                col.Add(f);
            }

            frames.Clear();
            foreach (List<FrameInfo> col in cols)
                frames.AddRange(col.OrderByDescending(f => f.Center.Y));
        }
    }
}