using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AGRDB.GenLay
{
    /// <summary>
    /// Dialogo unico de GENLAY.
    /// Secuencia: escoger capa -> seleccionar poligonos -> configurar -> generar.
    /// Construido por codigo (sin .Designer.cs ni .resx).
    /// </summary>
    public class GenLayForm : Form
    {
        private List<FrameInfo> _frames = new List<FrameInfo>();
        private HashSet<string> _existingLayouts;

        private ComboBox _cboLayer;
        private Button _btnPick;
        private Label _lblCount;
        private Label _lblPickInfo;

        private ComboBox _cboLayout;
        private Label _lblBaseInfo;
        private ComboBox _cboScale;
        private ComboBox _cboUnits;
        private TextBox _txtPrefix;
        private NumericUpDown _numStart;
        private RadioButton _rdoByColumns;
        private RadioButton _rdoByRows;
        private RadioButton _rdoUserPick;
        private ToolTip _tip;
        private CheckBox _chkResize;
        private ListBox _lstPreview;
        private Label _lblWarning;
        private Button _btnOk;

        // ------------------------------------------------------------------
        // Resultados que lee el comando
        // ------------------------------------------------------------------
        public List<FrameInfo> Frames { get { return _frames; } }
        public string BaseLayout { get; private set; }
        public double ScaleDenominator { get; private set; }
        public double PaperPerModel { get; private set; }
        public string Prefix { get; private set; }
        public int StartNumber { get; private set; }
        public FrameOrder FrameOrder { get; private set; }
        public bool ResizeViewport { get; private set; }

        private class UnitItem
        {
            public string Text;
            public double Factor;
            public override string ToString() { return Text; }
        }

        public GenLayForm()
        {
            BuildUi();
            LoadDrawingData();
            UpdateState();
        }

        // ------------------------------------------------------------------
        // INTERFAZ
        // ------------------------------------------------------------------
        private void BuildUi()
        {
            Text = "GENLAY - Generador de hojas";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 616);
            Font = SystemFonts.MessageBoxFont;

            // ---- PASO 1: capa y seleccion --------------------------------
            GroupBox grpSel = new GroupBox();
            grpSel.Bounds = new Rectangle(12, 8, 456, 128);
            grpSel.Text = "1. Marcos en el modelo";
            Controls.Add(grpSel);

            AddLabel(grpSel, "Capa de los marcos:", 14, 28);

            _cboLayer = new ComboBox();
            _cboLayer.Bounds = new Rectangle(150, 24, 292, 23);
            _cboLayer.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboLayer.DrawMode = DrawMode.OwnerDrawFixed;
            _cboLayer.ItemHeight = 18;
            _cboLayer.DrawItem += OnDrawLayerItem;
            _cboLayer.SelectedIndexChanged += delegate { OnLayerChanged(); };
            grpSel.Controls.Add(_cboLayer);

            _btnPick = new Button();
            _btnPick.Bounds = new Rectangle(150, 56, 292, 30);
            _btnPick.Text = "Seleccionar poligonos en el modelo...";
            _btnPick.Click += OnPickFrames;
            grpSel.Controls.Add(_btnPick);

            _lblCount = new Label();
            _lblCount.Bounds = new Rectangle(14, 94, 300, 26);
            _lblCount.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
            grpSel.Controls.Add(_lblCount);

            _lblPickInfo = new Label();
            _lblPickInfo.Bounds = new Rectangle(300, 98, 142, 20);
            _lblPickInfo.ForeColor = SystemColors.GrayText;
            _lblPickInfo.TextAlign = ContentAlignment.MiddleRight;
            grpSel.Controls.Add(_lblPickInfo);

            // ---- PASO 2: hoja y escala -----------------------------------
            GroupBox grpCfg = new GroupBox();
            grpCfg.Bounds = new Rectangle(12, 144, 456, 190);
            grpCfg.Text = "2. Hoja y escala";
            Controls.Add(grpCfg);

            AddLabel(grpCfg, "Layout base:", 14, 28);

            _cboLayout = new ComboBox();
            _cboLayout.Bounds = new Rectangle(150, 24, 292, 23);
            _cboLayout.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboLayout.SelectedIndexChanged += delegate { UpdateBaseInfo(); };
            grpCfg.Controls.Add(_cboLayout);

            _lblBaseInfo = new Label();
            _lblBaseInfo.Bounds = new Rectangle(150, 50, 292, 18);
            _lblBaseInfo.ForeColor = SystemColors.GrayText;
            grpCfg.Controls.Add(_lblBaseInfo);

            AddLabel(grpCfg, "Escala  1:", 14, 80);

            _cboScale = new ComboBox();
            _cboScale.Bounds = new Rectangle(150, 76, 110, 23);
            _cboScale.DropDownStyle = ComboBoxStyle.DropDown;
            _cboScale.Items.AddRange(new object[] { "25", "50", "75", "100", "200", "250", "500", "1000", "2000" });
            _cboScale.Text = "100";
            grpCfg.Controls.Add(_cboScale);

            AddLabel(grpCfg, "Unidades:", 275, 80);

            _cboUnits = new ComboBox();
            _cboUnits.Bounds = new Rectangle(340, 76, 102, 23);
            _cboUnits.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboUnits.Items.Add(new UnitItem { Text = "Metros", Factor = 1000.0 });
            _cboUnits.Items.Add(new UnitItem { Text = "Milimetros", Factor = 1.0 });
            _cboUnits.Items.Add(new UnitItem { Text = "Centimetros", Factor = 10.0 });
            _cboUnits.Items.Add(new UnitItem { Text = "Pies", Factor = 304.8 });
            _cboUnits.SelectedIndex = 0;
            grpCfg.Controls.Add(_cboUnits);

            AddLabel(grpCfg, "Prefijo de hoja:", 14, 112);

            _txtPrefix = new TextBox();
            _txtPrefix.Bounds = new Rectangle(150, 108, 110, 23);
            _txtPrefix.Text = AcadIo.DefaultPrefix;   // vacio: el 0 a la izquierda lo pone el relleno
            _txtPrefix.TextChanged += delegate { UpdatePreview(); };
            grpCfg.Controls.Add(_txtPrefix);

            AddLabel(grpCfg, "Iniciar en:", 275, 112);

            _numStart = new NumericUpDown();
            _numStart.Bounds = new Rectangle(340, 108, 102, 23);
            _numStart.Minimum = 1;
            _numStart.Maximum = 9999;
            _numStart.Value = 1;
            _numStart.ValueChanged += delegate { UpdatePreview(); };
            grpCfg.Controls.Add(_numStart);

            _tip = new ToolTip();
            _tip.AutoPopDelay = 12000;
            _tip.InitialDelay = 400;

            _rdoByColumns = new RadioButton();
            _rdoByColumns.Bounds = new Rectangle(16, 138, 130, 22);
            _rdoByColumns.Text = "Por columnas";
            _rdoByColumns.Checked = true;
            grpCfg.Controls.Add(_rdoByColumns);
            _tip.SetToolTip(_rdoByColumns,
                "Avance en X: baja cada columna de arriba a abajo y luego pasa a la de la derecha.\r\n"
                + "Cada proceso sale en un bloque contiguo (1,2,3,4  -  50,51,52,53).");

            _rdoByRows = new RadioButton();
            _rdoByRows.Bounds = new Rectangle(150, 138, 95, 22);
            _rdoByRows.Text = "Por filas";
            grpCfg.Controls.Add(_rdoByRows);
            _tip.SetToolTip(_rdoByRows,
                "Avance en Y: recorre cada fila de izquierda a derecha y luego baja a la siguiente.\r\n"
                + "Con dos columnas, alterna los procesos (1,50,2,51,...).");

            _rdoUserPick = new RadioButton();
            _rdoUserPick.Bounds = new Rectangle(250, 138, 150, 22);
            _rdoUserPick.Text = "Segun seleccion";
            grpCfg.Controls.Add(_rdoUserPick);
            _tip.SetToolTip(_rdoUserPick,
                "Respeta el orden en que seleccionaste los marcos, uno por uno.");

            _chkResize = new CheckBox();
            _chkResize.Bounds = new Rectangle(16, 162, 424, 22);
            _chkResize.Text = "Ajustar el tamano del viewport al marco";
            grpCfg.Controls.Add(_chkResize);

            // ---- PASO 3: vista previa ------------------------------------
            GroupBox grpPrev = new GroupBox();
            grpPrev.Bounds = new Rectangle(12, 342, 456, 190);
            grpPrev.Text = "3. Hojas que se generaran";
            Controls.Add(grpPrev);

            _lstPreview = new ListBox();
            _lstPreview.Bounds = new Rectangle(14, 22, 428, 136);
            _lstPreview.IntegralHeight = false;
            _lstPreview.SelectionMode = SelectionMode.None;
            grpPrev.Controls.Add(_lstPreview);

            _lblWarning = new Label();
            _lblWarning.Bounds = new Rectangle(14, 164, 428, 20);
            _lblWarning.ForeColor = Color.Firebrick;
            grpPrev.Controls.Add(_lblWarning);

            // ---- Botones --------------------------------------------------
            _btnOk = new Button();
            _btnOk.Bounds = new Rectangle(272, 544, 90, 30);
            _btnOk.Text = "Generar";
            _btnOk.Click += OnAccept;
            Controls.Add(_btnOk);

            Button btnCancel = new Button();
            btnCancel.Bounds = new Rectangle(370, 544, 90, 30);
            btnCancel.Text = "Cancelar";
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            CancelButton = btnCancel;
        }

        private static void AddLabel(Control parent, string text, int x, int y)
        {
            Label lbl = new Label();
            lbl.AutoSize = true;
            lbl.Location = new Point(x, y + 3);
            lbl.Text = text;
            parent.Controls.Add(lbl);
        }

        /// <summary>Dibuja el cuadrito de color junto al nombre de la capa.</summary>
        private void OnDrawLayerItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            if (e.Index < 0 || e.Index >= _cboLayer.Items.Count)
            {
                e.DrawFocusRectangle();
                return;
            }

            LayerInfo li = _cboLayer.Items[e.Index] as LayerInfo;
            if (li == null) { e.DrawFocusRectangle(); return; }

            int textLeft = e.Bounds.Left + 4;

            if (!li.IsAll)
            {
                Rectangle sw = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 3, 12, 12);
                using (SolidBrush b = new SolidBrush(li.Color))
                    e.Graphics.FillRectangle(b, sw);
                e.Graphics.DrawRectangle(Pens.Gray, sw);
                textLeft = e.Bounds.Left + 22;
            }

            string text = li.IsAll || li.Usable ? li.Name : li.Name + "   (apagada o congelada)";
            Color fore = li.Usable ? e.ForeColor : SystemColors.GrayText;

            using (SolidBrush tb = new SolidBrush(fore))
                e.Graphics.DrawString(text, e.Font, tb, textLeft, e.Bounds.Top + 1);

            e.DrawFocusRectangle();
        }

        // ------------------------------------------------------------------
        // CARGA DE DATOS DEL DIBUJO
        // ------------------------------------------------------------------
        private void LoadDrawingData()
        {
            // Capas
            foreach (LayerInfo li in AcadIo.GetLayers())
                _cboLayer.Items.Add(li);

            if (_cboLayer.Items.Count > 0) _cboLayer.SelectedIndex = 0;

            // Layouts
            List<string> layouts = AcadIo.GetLayoutNames();
            _existingLayouts = new HashSet<string>(layouts, StringComparer.OrdinalIgnoreCase);

            foreach (string name in layouts)
                _cboLayout.Items.Add(name);

            string current = AcadIo.GetCurrentLayoutName();
            int idx = current != null ? _cboLayout.FindStringExact(current) : -1;
            if (idx < 0 && _cboLayout.Items.Count > 0) idx = 0;
            if (idx >= 0) _cboLayout.SelectedIndex = idx;

            UpdateBaseInfo();
        }

        private string SelectedLayerName()
        {
            LayerInfo li = _cboLayer.SelectedItem as LayerInfo;
            if (li == null || li.IsAll) return null;
            return li.Name;
        }

        private void OnLayerChanged()
        {
            // Cambiar de capa invalida la seleccion anterior.
            if (_frames.Count > 0)
            {
                _frames.Clear();
                UpdateState();
            }

            LayerInfo li = _cboLayer.SelectedItem as LayerInfo;
            _btnPick.Enabled = li == null || li.Usable;
            _lblPickInfo.Text = (li != null && !li.Usable)
                ? "Capa no seleccionable"
                : string.Empty;
        }

        // ------------------------------------------------------------------
        // SELECCION DE MARCOS
        // ------------------------------------------------------------------
        private void OnPickFrames(object sender, EventArgs e)
        {
            string layer = SelectedLayerName();
            int skipped = 0;
            List<FrameInfo> picked = null;

            // Ocultar el dialogo para devolverle el editor al usuario.
            Visible = false;
            try
            {
                picked = AcadIo.PickFrames(layer, out skipped);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "No se pudo completar la seleccion:\n" + ex.Message,
                                "GENLAY", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Visible = true;
                BringToFront();
                Activate();
            }

            if (picked == null) return;   // ESC: se conserva lo que hubiera

            _frames = picked;
            _lblPickInfo.Text = skipped > 0
                ? skipped + " descartado(s)"
                : string.Empty;

            UpdateState();
        }

        // ------------------------------------------------------------------
        // ESTADO Y VISTA PREVIA
        // ------------------------------------------------------------------
        private void UpdateState()
        {
            int n = _frames.Count;

            _lblCount.Text = n == 0
                ? "Sin poligonos seleccionados"
                : string.Format("{0} vista{1} por crear", n, n == 1 ? "" : "s");

            _lblCount.ForeColor = n == 0 ? SystemColors.GrayText : SystemColors.ControlText;
            _btnOk.Enabled = n > 0;

            UpdateBaseInfo();
            UpdatePreview();
        }

        private void UpdateBaseInfo()
        {
            if (_lblBaseInfo == null) return;

            if (_cboLayout.SelectedItem == null)
            {
                _lblBaseInfo.Text = "El dibujo no tiene layouts de papel.";
                return;
            }

            _lblBaseInfo.Text = _frames.Count == 0
                ? string.Format("Se clonara \"{0}\".", _cboLayout.SelectedItem)
                : string.Format("Se clonara \"{0}\" {1} veces.", _cboLayout.SelectedItem, _frames.Count);
        }

        private void UpdatePreview()
        {
            if (_lstPreview == null) return;

            string prefix = _txtPrefix.Text;
            int start = (int)_numStart.Value;
            int conflicts = 0;

            _lstPreview.BeginUpdate();
            _lstPreview.Items.Clear();

            for (int i = 0; i < _frames.Count; i++)
            {
                string name = AcadIo.BuildSheetName(prefix, start + i);

                if (_existingLayouts != null && _existingLayouts.Contains(name))
                {
                    conflicts++;
                    _lstPreview.Items.Add(name + "   -  ya existe, se creara como " + name + "_2");
                }
                else
                {
                    _lstPreview.Items.Add(name);
                }
            }

            _lstPreview.EndUpdate();

            _lblWarning.Text = conflicts > 0
                ? string.Format("{0} nombre(s) ya existen en el dibujo.", conflicts)
                : string.Empty;
        }

        // ------------------------------------------------------------------
        // VALIDACION
        // ------------------------------------------------------------------
        private void OnAccept(object sender, EventArgs e)
        {
            if (_frames.Count == 0)
            {
                Warn("Primero selecciona los poligonos en el modelo.");
                return;
            }

            if (_cboLayout.SelectedItem == null)
            {
                Warn("Selecciona el layout base.");
                return;
            }

            double denom;
            if (!TryParseNumber(_cboScale.Text, out denom) || denom <= 0.0)
            {
                Warn("La escala debe ser un numero mayor que cero.");
                _cboScale.Focus();
                return;
            }

            // El prefijo es OPCIONAL: puede quedar vacio.
            // Los ceros a la izquierda los pone el relleno (NumberFormat), no el prefijo.
            string prefix = _txtPrefix.Text.Trim();

            const string invalid = "<>/\\\":;?*|,=`";
            foreach (char c in prefix)
            {
                if (invalid.IndexOf(c) >= 0)
                {
                    Warn("El prefijo no puede contener  < > / \\ \" : ; ? * | , = `");
                    _txtPrefix.Focus();
                    return;
                }
            }

            UnitItem unit = _cboUnits.SelectedItem as UnitItem;

            BaseLayout = _cboLayout.SelectedItem.ToString();
            ScaleDenominator = denom;
            PaperPerModel = unit != null ? unit.Factor : 1000.0;
            Prefix = prefix;
            StartNumber = (int)_numStart.Value;
            FrameOrder = _rdoByRows.Checked ? FrameOrder.ByRows
                       : _rdoUserPick.Checked ? FrameOrder.UserPick
                       : FrameOrder.ByColumns;
            ResizeViewport = _chkResize.Checked;

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Acepta coma o punto como separador decimal.</summary>
        private static bool TryParseNumber(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
                return true;

            return double.TryParse(text.Replace(',', '.'), NumberStyles.Any,
                                   CultureInfo.InvariantCulture, out value);
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, "GENLAY",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}