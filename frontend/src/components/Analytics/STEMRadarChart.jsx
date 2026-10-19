/**
 * @file STEMRadarChart — class-wide STEM competency radar, CSV export and certificates.
 */
import React, { useCallback, useEffect, useRef, useState } from 'react';
import PropTypes from 'prop-types';
import { Legend, PolarAngleAxis, PolarGrid, PolarRadiusAxis, Radar, RadarChart, ResponsiveContainer, Tooltip } from 'recharts';
import { jsPDF } from 'jspdf';
import Panel, { Spinner } from '../common/Panel';
import { api, downloadBlob, downloadFromApi, getErrorMessage } from '../../utils/api';
import { COLORS, studentShape } from '../../utils/format';

/** Polling interval for class telemetry. */
const REFRESH_MS = 30000;

/** Radar axes (keys match backend STEM_COMPETENCIES). */
const AXES = [
  { key: 'computationalThinking', label: 'Computational Thinking' },
  { key: 'systemicEngineering', label: 'Systemic Engineering' },
  { key: 'mathModeling', label: 'Math Modeling' },
  { key: 'persistence', label: 'Persistence' }
];

/**
 * Convert backend classAverages (0..1, or null when no data) into Recharts rows (0..100).
 * @param {object|undefined} averages
 * @returns {{axis: string, score: number, hasData: boolean}[]}
 */
function toChartData(averages) {
  return AXES.map(({ key, label }) => {
    const v = averages ? averages[key] : null;
    const hasData = typeof v === 'number' && Number.isFinite(v);
    return { axis: label, score: hasData ? Math.round(v * 100) : 0, hasData };
  });
}

/**
 * Client-side fallback certificate (used only when the server PDF cannot be produced).
 * @param {object} student
 * @param {string} roomPin
 */
function buildFallbackCertificate(student, roomPin) {
  const doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });
  const w = doc.internal.pageSize.getWidth();
  const h = doc.internal.pageSize.getHeight();
  doc.setFillColor(13, 27, 42);
  doc.rect(0, 0, w, h, 'F');
  doc.setDrawColor(46, 204, 113);
  doc.setLineWidth(6);
  doc.rect(24, 24, w - 48, h - 48);
  doc.setTextColor(46, 204, 113);
  doc.setFont('helvetica', 'bold');
  doc.setFontSize(40);
  doc.text('Certificate of Achievement', w / 2, 130, { align: 'center' });
  doc.setTextColor(232, 241, 248);
  doc.setFont('helvetica', 'normal');
  doc.setFontSize(18);
  doc.text('Eco-Bot Builders proudly recognises', w / 2, 190, { align: 'center' });
  doc.setFont('helvetica', 'bold');
  doc.setFontSize(36);
  doc.setTextColor(243, 156, 18);
  doc.text(student.name, w / 2, 250, { align: 'center' });
  doc.setFont('helvetica', 'normal');
  doc.setFontSize(16);
  doc.setTextColor(232, 241, 248);
  doc.text(
    `for building a cleaner world - reached Level ${student.currentLevel || 1} and earned ${student.cleanTokensBalance || 0} Clean Tokens.`,
    w / 2,
    300,
    { align: 'center', maxWidth: w - 160 }
  );
  doc.setFontSize(12);
  doc.setTextColor(143, 163, 191);
  doc.text(`Classroom ${roomPin}  |  ${new Date().toLocaleDateString()}`, w / 2, h - 70, { align: 'center' });
  downloadBlob(doc.output('blob'), `EcoBot_Certificate_${student.name.replace(/[^\w-]+/g, '_')}.pdf`);
}

/**
 * @param {object} props
 * @param {string} props.roomPin Classroom PIN.
 * @param {object[]} props.students Roster (for the certificate picker).
 * @param {number} [props.refreshKey=0] Changing this forces an immediate re-fetch.
 * @returns {JSX.Element}
 */
function STEMRadarChart({ roomPin, students, refreshKey = 0 }) {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [csvBusy, setCsvBusy] = useState(false);
  const [actionMsg, setActionMsg] = useState('');
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedId, setSelectedId] = useState('');
  const [certBusy, setCertBusy] = useState(false);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  /** Fetch class telemetry aggregates. */
  const fetchData = useCallback(async (signal) => {
    try {
      const res = await api.get(`/telemetry/class/${roomPin}`, { signal });
      if (!mounted.current) return;
      setData(res.data);
      setError('');
    } catch (err) {
      if (signal && signal.aborted) return;
      if (mounted.current) setError(await getErrorMessage(err, 'Could not load analytics.'));
    } finally {
      if (mounted.current) setLoading(false);
    }
  }, [roomPin]);

  // Fetch on mount, whenever refreshKey changes, and every 30 seconds.
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    fetchData(controller.signal);
    const id = setInterval(() => fetchData(controller.signal), REFRESH_MS);
    return () => {
      controller.abort();
      clearInterval(id);
    };
  }, [fetchData, refreshKey]);

  /** Download the LMS CSV report. */
  const handleCsv = useCallback(async () => {
    setCsvBusy(true);
    setActionMsg('');
    try {
      await downloadFromApi(`/reports/export-csv/${roomPin}`, `EcoBot_Class_${roomPin}.csv`);
      setActionMsg('✅ CSV report downloaded.');
    } catch (err) {
      setActionMsg(`⚠️ ${await getErrorMessage(err, 'CSV export failed.')}`);
    } finally {
      setCsvBusy(false);
    }
  }, [roomPin]);

  /** Download the selected student's certificate (server PDF, jsPDF fallback on server/network failure). */
  const handleCertificate = useCallback(async () => {
    const student = students.find((s) => s.studentId === selectedId);
    if (!student) return;
    setCertBusy(true);
    setActionMsg('');
    try {
      await downloadFromApi(`/reports/certificate/${student.studentId}`, `EcoBot_Certificate_${student.name}.pdf`);
      setActionMsg(`✅ Certificate for ${student.name} downloaded.`);
      setDialogOpen(false);
    } catch (err) {
      const status = err && err.response ? err.response.status : 0;
      if (status === 0 || status >= 500) {
        buildFallbackCertificate(student, roomPin);
        setActionMsg(`ℹ️ Server unavailable — generated a basic certificate for ${student.name} in the browser.`);
        setDialogOpen(false);
      } else {
        setActionMsg(`⚠️ ${await getErrorMessage(err, 'Certificate generation failed.')}`);
      }
    } finally {
      setCertBusy(false);
    }
  }, [students, selectedId, roomPin]);

  const chartData = toChartData(data && data.classAverages);
  const hasAnyData = chartData.some((d) => d.hasData);

  return (
    <Panel
      id="stem-radar"
      title="📊 STEM Competencies"
      actions={loading ? <Spinner size={16} /> : null}
      style={styles.panel}
    >
      {error && <p role="alert" style={styles.error}>⚠️ {error}</p>}

      <div style={styles.chartBox}>
        {!data && loading ? (
          <div style={styles.center}><Spinner /></div>
        ) : (
          <ResponsiveContainer width="100%" height="100%">
            <RadarChart data={chartData} outerRadius="68%">
              <PolarGrid stroke={COLORS.border} />
              <PolarAngleAxis dataKey="axis" tick={{ fill: COLORS.text, fontSize: 12 }} />
              <PolarRadiusAxis angle={90} domain={[0, 100]} tick={{ fill: COLORS.muted, fontSize: 10 }} tickCount={5} />
              <Radar
                name="Class average"
                dataKey="score"
                stroke={COLORS.greenDark}
                strokeWidth={2}
                fill={COLORS.green}
                fillOpacity={0.3}
                isAnimationActive
              />
              <Tooltip
                contentStyle={{ background: COLORS.panelAlt, border: `1px solid ${COLORS.border}`, borderRadius: 8 }}
                labelStyle={{ color: COLORS.text }}
                formatter={(value, name, item) => [item && item.payload && item.payload.hasData ? `${value}%` : 'No data yet', name]}
              />
              <Legend wrapperStyle={{ color: COLORS.text, fontSize: 12 }} />
            </RadarChart>
          </ResponsiveContainer>
        )}
      </div>

      {data && (
        <p style={styles.meta}>
          {hasAnyData
            ? `Based on ${data.recordCount} level completion${data.recordCount === 1 ? '' : 's'} from ${data.studentCount} student${data.studentCount === 1 ? '' : 's'}.`
            : 'No completed levels yet — scores will appear as students finish levels.'}
        </p>
      )}

      <div style={styles.buttons}>
        <button type="button" style={styles.button} onClick={handleCsv} disabled={csvBusy}>
          {csvBusy ? 'Exporting…' : '⬇️ Download CSV Report'}
        </button>
        <button
          type="button"
          style={{ ...styles.button, ...styles.buttonAlt }}
          onClick={() => {
            setSelectedId(students[0] ? students[0].studentId : '');
            setDialogOpen(true);
          }}
          disabled={students.length === 0}
          title={students.length === 0 ? 'No students in this room yet' : undefined}
        >
          🎓 Generate Certificate
        </button>
      </div>
      {actionMsg && <p style={styles.actionMsg} aria-live="polite">{actionMsg}</p>}

      {dialogOpen && (
        <div style={styles.backdrop} role="presentation" onClick={() => !certBusy && setDialogOpen(false)}>
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="cert-dialog-title"
            style={styles.dialog}
            onClick={(e) => e.stopPropagation()}
          >
            <h3 id="cert-dialog-title" style={styles.dialogTitle}>Generate Certificate</h3>
            <label htmlFor="cert-student" style={styles.label}>Select a student</label>
            <select
              id="cert-student"
              value={selectedId}
              onChange={(e) => setSelectedId(e.target.value)}
              style={styles.select}
              disabled={certBusy}
            >
              {students.map((s) => (
                <option key={s.studentId} value={s.studentId}>
                  {s.name} — Level {s.currentLevel || 1}
                </option>
              ))}
            </select>
            <div style={styles.dialogButtons}>
              <button type="button" style={{ ...styles.button, ...styles.buttonGhost }} onClick={() => setDialogOpen(false)} disabled={certBusy}>
                Cancel
              </button>
              <button type="button" style={styles.button} onClick={handleCertificate} disabled={certBusy || !selectedId}>
                {certBusy ? 'Generating…' : 'Download PDF'}
              </button>
            </div>
          </div>
        </div>
      )}
    </Panel>
  );
}

STEMRadarChart.propTypes = {
  roomPin: PropTypes.string.isRequired,
  students: PropTypes.arrayOf(studentShape).isRequired,
  refreshKey: PropTypes.number
};

const styles = {
  panel: { flex: '1 1 auto', minHeight: 420 },
  error: { color: COLORS.amber, fontSize: 13, margin: '0 0 8px' },
  chartBox: { width: '100%', height: 280 },
  center: { height: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center' },
  meta: { color: COLORS.muted, fontSize: 12, textAlign: 'center', margin: '4px 0 10px' },
  buttons: { display: 'flex', gap: 8, flexWrap: 'wrap' },
  button: {
    flex: '1 1 140px',
    minHeight: 44,
    borderRadius: 10,
    border: 'none',
    background: COLORS.green,
    color: '#06210F',
    fontWeight: 700,
    fontSize: 14,
    cursor: 'pointer',
    padding: '0 12px'
  },
  buttonAlt: { background: COLORS.amber, color: '#1E1404' },
  buttonGhost: { background: 'transparent', color: COLORS.text, border: `1px solid ${COLORS.border}` },
  actionMsg: { fontSize: 13, color: COLORS.text, margin: '10px 0 0' },
  backdrop: {
    position: 'fixed',
    inset: 0,
    background: 'rgba(0,0,0,0.6)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    zIndex: 1000,
    padding: 16
  },
  dialog: {
    width: '100%',
    maxWidth: 380,
    background: COLORS.panel,
    border: `1px solid ${COLORS.border}`,
    borderRadius: 14,
    padding: 22,
    animation: 'eco-slide-in 0.2s ease-out'
  },
  dialogTitle: { margin: '0 0 14px', color: COLORS.green },
  label: { display: 'block', fontSize: 14, marginBottom: 6 },
  select: {
    width: '100%',
    padding: '10px 12px',
    fontSize: 15,
    borderRadius: 8,
    border: `1px solid ${COLORS.border}`,
    background: COLORS.bg,
    color: COLORS.text
  },
  dialogButtons: { display: 'flex', gap: 8, marginTop: 18 }
};

export default STEMRadarChart;
