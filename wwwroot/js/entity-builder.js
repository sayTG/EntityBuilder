document.addEventListener('DOMContentLoaded', function () {
    const state = {
        mainTable: null,
        columns: {},
        currentPage: 1,
        pageSize: 50
    };

    const tables = JSON.parse(document.getElementById('tableListData')?.textContent || '[]');

    const mainTableSelect = document.getElementById('mainTable');
    const addJoinBtn = document.getElementById('addJoin');
    const addWhereBtn = document.getElementById('addWhere');
    const addGroupByBtn = document.getElementById('addGroupBy');
    const addAggregateBtn = document.getElementById('addAggregate');
    const addOrderByBtn = document.getElementById('addOrderBy');
    const executeBtn = document.getElementById('executeQuery');
    const sendReportBtn = document.getElementById('sendReportEmail');
    const reportSpinner = document.getElementById('reportSpinner');
    const joinsContainer = document.getElementById('joinsContainer');
    const whereContainer = document.getElementById('whereContainer');
    const groupByContainer = document.getElementById('groupByContainer');
    const orderByContainer = document.getElementById('orderByContainer');
    const columnsContainer = document.getElementById('columnsContainer');
    const resultsSection = document.getElementById('resultsSection');
    const loadingSpinner = document.getElementById('loadingSpinner');

    // Chart elements
    const viewModeTabs = document.getElementById('viewModeTabs');
    const chartConfigPanel = document.getElementById('chartConfigPanel');
    const chartLabelSelect = document.getElementById('chartLabelColumn');
    const chartValueSelect = document.getElementById('chartValueColumn');
    const barChartContainer = document.getElementById('barChartContainer');
    const pieChartContainer = document.getElementById('pieChartContainer');
    const barChartCanvas = document.getElementById('barChartCanvas');
    const pieChartCanvas = document.getElementById('pieChartCanvas');

    let barChartInstance = null;
    let pieChartInstance = null;
    let lastResultData = null;
    let lastQueryParameters = null;
    let lastQueryDefinition = null; // last request sent to /ExecuteQuery — used as the authoritative structured definition when scheduling
    let currentView = 'grid';

    // ========== SEARCHABLE SELECT COMPONENT ==========
    let activeSearchable = null;

    function createSearchableSelect(selectEl) {
        if (selectEl.dataset.searchable === 'init') return;
        selectEl.dataset.searchable = 'init';
        selectEl.style.display = 'none';

        const wrapper = document.createElement('div');
        wrapper.className = 'eb-searchable-select';

        const display = document.createElement('div');
        display.className = 'eb-ss-display';
        display.innerHTML = `<span class="eb-ss-text">${selectEl.options[selectEl.selectedIndex]?.text || '-- Select --'}</span><i class="bi bi-chevron-down eb-ss-arrow"></i>`;

        const dropdown = document.createElement('div');
        dropdown.className = 'eb-ss-dropdown';
        dropdown.innerHTML = `<input type="text" class="eb-ss-search" placeholder="Type to search...">
            <div class="eb-ss-options"></div>`;

        wrapper.appendChild(display);
        wrapper.appendChild(dropdown);
        selectEl.parentNode.insertBefore(wrapper, selectEl.nextSibling);

        const searchInput = dropdown.querySelector('.eb-ss-search');
        const optionsContainer = dropdown.querySelector('.eb-ss-options');

        function buildOptions(filter = '') {
            const lf = filter.toLowerCase();
            let html = '';
            // Handle optgroups
            const optgroups = selectEl.querySelectorAll('optgroup');
            if (optgroups.length > 0) {
                // First render ungrouped options
                selectEl.querySelectorAll(':scope > option').forEach(opt => {
                    if (lf && !opt.text.toLowerCase().includes(lf)) return;
                    const selected = opt.value === selectEl.value ? ' eb-ss-active' : '';
                    html += `<div class="eb-ss-option${selected}" data-value="${opt.value}">${escapeHtml(opt.text)}</div>`;
                });
                optgroups.forEach(group => {
                    let groupHtml = '';
                    group.querySelectorAll('option').forEach(opt => {
                        if (lf && !opt.text.toLowerCase().includes(lf) && !group.label.toLowerCase().includes(lf)) return;
                        const selected = opt.value === selectEl.value ? ' eb-ss-active' : '';
                        groupHtml += `<div class="eb-ss-option${selected}" data-value="${opt.value}">${escapeHtml(opt.text)}</div>`;
                    });
                    if (groupHtml) {
                        html += `<div class="eb-ss-group">${escapeHtml(group.label)}</div>` + groupHtml;
                    }
                });
            } else {
                Array.from(selectEl.options).forEach(opt => {
                    if (lf && !opt.text.toLowerCase().includes(lf)) return;
                    const selected = opt.value === selectEl.value ? ' eb-ss-active' : '';
                    html += `<div class="eb-ss-option${selected}" data-value="${opt.value}">${escapeHtml(opt.text)}</div>`;
                });
            }
            if (!html) html = '<div class="eb-ss-empty">No results found</div>';
            optionsContainer.innerHTML = html;
        }

        function open() {
            if (activeSearchable && activeSearchable !== wrapper) closeAll();
            activeSearchable = wrapper;
            wrapper.classList.add('eb-ss-open');
            buildOptions();
            searchInput.value = '';
            setTimeout(() => searchInput.focus(), 10);
        }

        function close() {
            wrapper.classList.remove('eb-ss-open');
            if (activeSearchable === wrapper) activeSearchable = null;
        }

        display.addEventListener('click', (e) => {
            e.stopPropagation();
            wrapper.classList.contains('eb-ss-open') ? close() : open();
        });

        searchInput.addEventListener('input', () => buildOptions(searchInput.value));
        searchInput.addEventListener('click', (e) => e.stopPropagation());

        optionsContainer.addEventListener('click', (e) => {
            const opt = e.target.closest('.eb-ss-option');
            if (!opt) return;
            e.stopPropagation();
            selectEl.value = opt.dataset.value;
            display.querySelector('.eb-ss-text').textContent = opt.textContent;
            selectEl.dispatchEvent(new Event('change'));
            close();
        });

        // Sync when select is updated programmatically
        const observer = new MutationObserver(() => {
            display.querySelector('.eb-ss-text').textContent =
                selectEl.options[selectEl.selectedIndex]?.text || '-- Select --';
        });
        observer.observe(selectEl, { childList: true, subtree: true, attributes: true });

        // Public method to refresh display
        wrapper._refresh = function () {
            display.querySelector('.eb-ss-text').textContent =
                selectEl.options[selectEl.selectedIndex]?.text || '-- Select --';
        };

        return wrapper;
    }

    function closeAll() {
        document.querySelectorAll('.eb-ss-open').forEach(el => el.classList.remove('eb-ss-open'));
        activeSearchable = null;
    }

    document.addEventListener('click', closeAll);

    function initSearchableSelects(container) {
        container.querySelectorAll('select').forEach(sel => createSearchableSelect(sel));
    }

    function refreshSearchableSelect(selectEl) {
        const wrapper = selectEl.nextElementSibling;
        if (wrapper && wrapper.classList.contains('eb-searchable-select')) {
            wrapper._refresh();
        }
    }

    // Init the main table dropdown
    if (mainTableSelect) createSearchableSelect(mainTableSelect);

    // ========== HELPERS ==========
    function getAntiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    function parseTableValue(val) {
        if (!val) return null;
        const parts = val.split('|');
        return { schema: parts[0], table: parts[1] };
    }

    async function fetchColumns(schema, table) {
        const key = `${schema}.${table}`;
        if (state.columns[key]) return state.columns[key];
        const resp = await fetch(`/EntityBuilder/GetColumns?schema=${encodeURIComponent(schema)}&table=${encodeURIComponent(table)}`);
        if (!resp.ok) return [];
        const cols = await resp.json();
        state.columns[key] = cols;
        return cols;
    }

    function buildTableOptions() {
        return tables.map(t =>
            `<option value="${t.schemaName}|${t.tableName}">[${t.schemaName}].[${t.tableName}]</option>`
        ).join('');
    }

    function getSelectedTables() {
        const result = [];
        if (state.mainTable) {
            result.push({ schema: state.mainTable.schema, table: state.mainTable.table, label: state.mainTable.table });
        }
        document.querySelectorAll('.qb-join-row').forEach(row => {
            const tableVal = row.querySelector('.join-table')?.value;
            const parsed = parseTableValue(tableVal);
            if (parsed) {
                const dup = result.some(r => r.table === parsed.table && r.schema !== parsed.schema);
                result.push({
                    schema: parsed.schema, table: parsed.table,
                    label: dup ? `${parsed.schema}.${parsed.table}` : parsed.table
                });
                if (dup) result.forEach(r => {
                    if (r.table === parsed.table && r.schema !== parsed.schema && !r.label.includes('.'))
                        r.label = `${r.schema}.${r.table}`;
                });
            }
        });
        const cnt = {};
        result.forEach(r => { cnt[r.label] = (cnt[r.label] || 0) + 1; });
        const seen = {};
        result.forEach(r => {
            if (cnt[r.label] > 1) { seen[r.label] = (seen[r.label] || 0) + 1; r.label = `${r.label} (${seen[r.label]})`; }
        });
        return result;
    }

    function buildColumnOptions() {
        let html = '';
        getSelectedTables().forEach(t => {
            const key = `${t.schema}.${t.table}`;
            const cols = state.columns[key] || [];
            if (cols.length) {
                html += `<optgroup label="${t.label}">`;
                cols.forEach(c => {
                    html += `<option value="${t.schema}.${t.table}.${c.columnName}">[${t.label}].${c.columnName}</option>`;
                });
                html += '</optgroup>';
            }
        });
        return html;
    }

    // Build column options for the left side of a join at the given index.
    // Join 0 (first join) gets only main table columns.
    // Join 1 gets main table + join 0's table columns. Etc.
    function buildJoinLeftColumnOptions(joinIndex) {
        const tablesToInclude = [];
        if (state.mainTable) {
            tablesToInclude.push({ schema: state.mainTable.schema, table: state.mainTable.table, label: state.mainTable.table });
        }
        const joinRows = document.querySelectorAll('.qb-join-row');
        for (let i = 0; i < joinIndex && i < joinRows.length; i++) {
            const tableVal = joinRows[i].querySelector('.join-table')?.value;
            const parsed = parseTableValue(tableVal);
            if (parsed) {
                tablesToInclude.push({ schema: parsed.schema, table: parsed.table, label: parsed.table });
            }
        }
        let html = '';
        tablesToInclude.forEach(t => {
            const key = `${t.schema}.${t.table}`;
            const cols = state.columns[key] || [];
            if (cols.length) {
                html += `<optgroup label="${t.label}">`;
                cols.forEach(c => {
                    html += `<option value="${t.schema}.${t.table}.${c.columnName}">[${t.label}].${c.columnName}</option>`;
                });
                html += '</optgroup>';
            }
        });
        return html;
    }

    function buildColumnOptionsWithStar() {
        return '<option value="*">* (All)</option>' + buildColumnOptions();
    }

    function refreshAllDropdowns() {
        const opts = buildColumnOptions();
        const refreshSelects = (selector) => {
            document.querySelectorAll(selector).forEach(sel => {
                const cur = sel.value;
                const placeholder = sel.querySelector('option[value=""]')?.textContent || '-- Select --';
                sel.innerHTML = `<option value="">${placeholder}</option>${opts}`;
                if (cur) sel.value = cur;
                refreshSearchableSelect(sel);
            });
        };
        document.querySelectorAll('.join-left-col').forEach((sel, idx) => {
            const cur = sel.value;
            sel.innerHTML = `<option value="">-- Left Column --</option>${buildJoinLeftColumnOptions(idx)}`;
            if (cur) sel.value = cur;
            refreshSearchableSelect(sel);
        });
        refreshSelects('.where-column');
        refreshSelects('.groupby-column');
        refreshSelects('.orderby-column');

        const optsWithStar = buildColumnOptionsWithStar();
        document.querySelectorAll('.agg-column').forEach(sel => {
            const cur = sel.value;
            sel.innerHTML = `<option value="">-- Column --</option>${optsWithStar}`;
            if (cur) sel.value = cur;
            refreshSearchableSelect(sel);
        });

        refreshSelectColumns();
    }

    function refreshSelectColumns() {
        const cols = [];
        getSelectedTables().forEach(t => {
            const key = `${t.schema}.${t.table}`;
            (state.columns[key] || []).forEach(c => {
                cols.push({ value: `${t.schema}.${t.table}.${c.columnName}`, text: `[${t.label}].${c.columnName}`, schema: t.schema, table: t.table, column: c.columnName });
            });
        });

        const badge = document.getElementById('colCountBadge');

        if (!cols.length) {
            columnsContainer.innerHTML = '<p class="eb-empty-message"><i class="bi bi-info-circle me-1"></i> Select a main table to see available columns.</p>';
            if (badge) badge.textContent = '';
            return;
        }

        const hasGroupBy = groupByContainer.querySelectorAll('.qb-groupby-row, .qb-aggregate-row').length > 0;
        if (hasGroupBy) {
            columnsContainer.innerHTML = '<p class="eb-empty-message"><i class="bi bi-info-circle me-1"></i> Column selection is managed by GROUP BY and aggregates.</p>';
            if (badge) badge.textContent = '';
            return;
        }

        const prevChecked = new Set();
        let hadPrev = false;
        document.querySelectorAll('.col-check').forEach(cb => { hadPrev = true; if (cb.checked) prevChecked.add(cb.value); });
        const selectAllWas = document.getElementById('selectAllCols')?.checked ?? true;

        let html = '<div class="qb-column-checks">';
        html += `<div class="form-check"><input class="form-check-input" type="checkbox" id="selectAllCols" ${selectAllWas ? 'checked' : ''}>
            <label class="form-check-label fw-bold" for="selectAllCols">Select All</label></div>`;

        let idx = 0;
        getSelectedTables().forEach(t => {
            const key = `${t.schema}.${t.table}`;
            (state.columns[key] || []).forEach(c => {
                const val = `${t.schema}.${t.table}.${c.columnName}`;
                const chk = hadPrev ? prevChecked.has(val) || selectAllWas : true;
                html += `<div class="form-check"><input class="form-check-input col-check" type="checkbox" value="${val}" id="col_${idx}" ${chk ? 'checked' : ''}
                    data-schema="${t.schema}" data-table="${t.table}" data-column="${c.columnName}">
                    <label class="form-check-label" for="col_${idx}">[${t.label}].${c.columnName}</label></div>`;
                idx++;
            });
        });
        html += '</div>';
        columnsContainer.innerHTML = html;
        if (badge) badge.textContent = `${idx} columns`;

        document.getElementById('selectAllCols')?.addEventListener('change', function () {
            document.querySelectorAll('.col-check').forEach(cb => cb.checked = this.checked);
        });
    }

    // ========== MAIN TABLE CHANGE ==========
    mainTableSelect?.addEventListener('change', async function () {
        const parsed = parseTableValue(this.value);
        state.mainTable = parsed;

        joinsContainer.innerHTML = '<p class="eb-empty-message" id="noJoinsMessage"><i class="bi bi-info-circle me-1"></i> No joins added.</p>';
        whereContainer.innerHTML = '<p class="eb-empty-message" id="noWhereMessage"><i class="bi bi-info-circle me-1"></i> No conditions added.</p>';
        groupByContainer.innerHTML = '<p class="eb-empty-message" id="noGroupByMessage"><i class="bi bi-info-circle me-1"></i> No grouping.</p>';
        orderByContainer.innerHTML = '<p class="eb-empty-message" id="noOrderByMessage"><i class="bi bi-info-circle me-1"></i> No sorting applied.</p>';

        const scheduleReportBtn = document.getElementById('scheduleReport');
        const btns = [addJoinBtn, addWhereBtn, addGroupByBtn, addAggregateBtn, addOrderByBtn, executeBtn, sendReportBtn, scheduleReportBtn];
        if (parsed) {
            await fetchColumns(parsed.schema, parsed.table);
            btns.forEach(b => { if (b) b.disabled = false; });
            refreshSelectColumns();
        } else {
            btns.forEach(b => { if (b) b.disabled = true; });
            columnsContainer.innerHTML = '<p class="eb-empty-message"><i class="bi bi-info-circle me-1"></i> Select a main table.</p>';
        }
    });

    // ========== ADD JOIN ==========
    addJoinBtn?.addEventListener('click', function () {
        document.getElementById('noJoinsMessage')?.remove();
        const row = document.createElement('div');
        row.className = 'qb-join-row';
        row.innerHTML = `
            <select class="join-type">
                <option value="INNER JOIN">INNER JOIN</option>
                <option value="LEFT JOIN">LEFT JOIN</option>
                <option value="RIGHT JOIN">RIGHT JOIN</option>
                <option value="FULL OUTER JOIN">FULL OUTER JOIN</option>
            </select>
            <select class="join-table"><option value="">-- Table --</option>${buildTableOptions()}</select>
            <span class="qb-on-label">ON</span>
            <select class="join-left-col"><option value="">-- Left Column --</option>${buildJoinLeftColumnOptions(joinsContainer.querySelectorAll('.qb-join-row').length)}</select>
            <span class="qb-equals-label">=</span>
            <select class="join-right-col"><option value="">-- Right Column --</option></select>
            <button class="eb-btn-remove" title="Remove"><i class="bi bi-x"></i></button>`;
        joinsContainer.appendChild(row);
        initSearchableSelects(row);

        const joinTableSel = row.querySelector('.join-table');
        const rightColSel = row.querySelector('.join-right-col');

        joinTableSel.addEventListener('change', async function () {
            const parsed = parseTableValue(this.value);
            if (parsed) {
                const cols = await fetchColumns(parsed.schema, parsed.table);
                rightColSel.innerHTML = '<option value="">-- Right Column --</option>' +
                    cols.map(c => `<option value="${c.columnName}">[${parsed.table}].${c.columnName}</option>`).join('');
                refreshSearchableSelect(rightColSel);
                refreshAllDropdowns();
            } else {
                rightColSel.innerHTML = '<option value="">-- Right Column --</option>';
                refreshSearchableSelect(rightColSel);
                refreshAllDropdowns();
            }
        });

        row.querySelector('.eb-btn-remove').addEventListener('click', function () {
            row.remove();
            if (!joinsContainer.children.length) joinsContainer.innerHTML = '<p class="eb-empty-message" id="noJoinsMessage"><i class="bi bi-info-circle me-1"></i> No joins added.</p>';
            refreshAllDropdowns();
        });
    });

    // Shared markup for the mutually-exclusive NOW / TODAY toggles used by both the main
    // WHERE row and the schedule-modal WHERE row.
    const VALUE_KIND_BUTTONS_HTML = `
        <button type="button" class="eb-btn-when" data-mode="Now" title="Use current date + time (resolved by DB at run time)" aria-pressed="false">NOW</button>
        <button type="button" class="eb-btn-when" data-mode="Today" title="Use current date only (resolved by DB at run time)" aria-pressed="false">TODAY</button>`;

    // Wire the NOW / TODAY / literal state onto a WHERE row.
    // Returns { setMode(mode) } so callers can pre-fill from stored conditions.
    // Modes: null (literal), 'Now', 'Today'.
    function wireValueKindToggles(row, opSel, valInput) {
        const buttons = Array.from(row.querySelectorAll('.eb-btn-when'));
        const isNoValueOp = () => opSel.value === 'IS NULL' || opSel.value === 'IS NOT NULL';
        const isMultiValueOp = () => opSel.value === 'IN';
        const currentMode = () => buttons.find(b => b.getAttribute('aria-pressed') === 'true')?.dataset.mode || null;

        const applyState = () => {
            const mode = currentMode();
            if (mode) {
                valInput.value = '';
                valInput.placeholder = mode === 'Today' ? 'TODAY (date) — resolved at run time' : 'NOW — resolved at run time';
                valInput.disabled = true;
            } else {
                valInput.placeholder = 'Value';
                valInput.disabled = false;
            }
        };

        buttons.forEach(btn => btn.addEventListener('click', function () {
            if (isNoValueOp() || isMultiValueOp()) return;
            const wasOn = btn.getAttribute('aria-pressed') === 'true';
            buttons.forEach(b => b.setAttribute('aria-pressed', 'false'));
            if (!wasOn) btn.setAttribute('aria-pressed', 'true');
            applyState();
        }));

        opSel.addEventListener('change', function () {
            const noVal = isNoValueOp();
            valInput.style.display = noVal ? 'none' : '';
            const hide = noVal || isMultiValueOp();
            buttons.forEach(b => b.style.display = hide ? 'none' : '');
            if (hide) {
                buttons.forEach(b => b.setAttribute('aria-pressed', 'false'));
                applyState();
            }
            if (valInput.style.display === 'none') valInput.value = '';
        });

        return {
            setMode(mode) {
                buttons.forEach(b => b.setAttribute('aria-pressed', b.dataset.mode === mode ? 'true' : 'false'));
                applyState();
            },
            currentMode
        };
    }

    // ========== ADD WHERE ==========
    addWhereBtn?.addEventListener('click', function () {
        document.getElementById('noWhereMessage')?.remove();
        const isFirst = !whereContainer.querySelectorAll('.qb-where-row').length;
        const row = document.createElement('div');
        row.className = 'qb-where-row';
        row.innerHTML = `
            <select class="where-connector" ${isFirst ? 'style="visibility:hidden"' : ''}>
                <option value="AND">AND</option><option value="OR">OR</option>
            </select>
            <select class="where-column"><option value="">-- Column --</option>${buildColumnOptions()}</select>
            <select class="where-operator">
                <option value="=">=</option><option value="!=">!=</option>
                <option value=">">&gt;</option><option value="<">&lt;</option>
                <option value=">=">&gt;=</option><option value="<=">&lt;=</option>
                <option value="LIKE">LIKE</option><option value="IN">IN</option>
                <option value="IS NULL">IS NULL</option><option value="IS NOT NULL">IS NOT NULL</option>
            </select>
            <input type="text" class="where-value" placeholder="Value">
            ${VALUE_KIND_BUTTONS_HTML}
            <button class="eb-btn-remove" title="Remove"><i class="bi bi-x"></i></button>`;
        whereContainer.appendChild(row);
        initSearchableSelects(row);

        const opSel = row.querySelector('.where-operator');
        const valInput = row.querySelector('.where-value');
        wireValueKindToggles(row, opSel, valInput);

        row.querySelector('.eb-btn-remove').addEventListener('click', function () {
            row.remove();
            const first = whereContainer.querySelector('.qb-where-row');
            if (first) first.querySelector('.where-connector').style.visibility = 'hidden';
            if (!whereContainer.querySelectorAll('.qb-where-row').length) whereContainer.innerHTML = '<p class="eb-empty-message" id="noWhereMessage"><i class="bi bi-info-circle me-1"></i> No conditions added.</p>';
        });
    });

    // ========== ADD GROUP BY ==========
    addGroupByBtn?.addEventListener('click', function () {
        document.getElementById('noGroupByMessage')?.remove();
        const row = document.createElement('div');
        row.className = 'qb-groupby-row';
        row.innerHTML = `
            <span class="qb-on-label">GROUP BY</span>
            <select class="groupby-column"><option value="">-- Column --</option>${buildColumnOptions()}</select>
            <button class="eb-btn-remove" title="Remove"><i class="bi bi-x"></i></button>`;
        groupByContainer.appendChild(row);
        initSearchableSelects(row);
        refreshSelectColumns();

        row.querySelector('.eb-btn-remove').addEventListener('click', function () {
            row.remove();
            if (!groupByContainer.querySelectorAll('.qb-groupby-row, .qb-aggregate-row').length)
                groupByContainer.innerHTML = '<p class="eb-empty-message" id="noGroupByMessage"><i class="bi bi-info-circle me-1"></i> No grouping.</p>';
            refreshSelectColumns();
        });
    });

    // ========== ADD AGGREGATE ==========
    addAggregateBtn?.addEventListener('click', function () {
        document.getElementById('noGroupByMessage')?.remove();
        const row = document.createElement('div');
        row.className = 'qb-aggregate-row';
        row.innerHTML = `
            <select class="agg-function">
                <option value="COUNT">COUNT</option><option value="SUM">SUM</option>
                <option value="AVG">AVG</option><option value="MIN">MIN</option><option value="MAX">MAX</option>
            </select>
            <span class="qb-on-label">(</span>
            <select class="agg-column"><option value="">-- Column --</option>${buildColumnOptionsWithStar()}</select>
            <span class="qb-on-label">)</span>
            <span class="qb-on-label">AS</span>
            <input type="text" class="agg-alias" placeholder="Alias">
            <button class="eb-btn-remove" title="Remove"><i class="bi bi-x"></i></button>`;
        groupByContainer.appendChild(row);
        initSearchableSelects(row);
        refreshSelectColumns();

        row.querySelector('.eb-btn-remove').addEventListener('click', function () {
            row.remove();
            if (!groupByContainer.querySelectorAll('.qb-groupby-row, .qb-aggregate-row').length)
                groupByContainer.innerHTML = '<p class="eb-empty-message" id="noGroupByMessage"><i class="bi bi-info-circle me-1"></i> No grouping.</p>';
            refreshSelectColumns();
        });
    });

    // ========== ADD ORDER BY ==========
    addOrderByBtn?.addEventListener('click', function () {
        document.getElementById('noOrderByMessage')?.remove();
        const row = document.createElement('div');
        row.className = 'qb-orderby-row';
        row.innerHTML = `
            <span class="qb-on-label">ORDER BY</span>
            <select class="orderby-column"><option value="">-- Column --</option>${buildColumnOptions()}</select>
            <select class="orderby-dir">
                <option value="ASC">ASC &#x2191;</option><option value="DESC">DESC &#x2193;</option>
            </select>
            <button class="eb-btn-remove" title="Remove"><i class="bi bi-x"></i></button>`;
        orderByContainer.appendChild(row);
        initSearchableSelects(row);

        row.querySelector('.eb-btn-remove').addEventListener('click', function () {
            row.remove();
            if (!orderByContainer.querySelectorAll('.qb-orderby-row').length)
                orderByContainer.innerHTML = '<p class="eb-empty-message" id="noOrderByMessage"><i class="bi bi-info-circle me-1"></i> No sorting applied.</p>';
        });
    });

    // ========== EXECUTE ==========
    executeBtn?.addEventListener('click', function () {
        state.currentPage = 1;
        executeQuery();
    });

    async function executeQuery() {
        if (!state.mainTable) return;

        const request = {
            schema: state.mainTable.schema, table: state.mainTable.table,
            joins: [], whereConditions: [], selectedColumns: [],
            groupByColumns: [], aggregateColumns: [], orderByColumns: [],
            page: state.currentPage, pageSize: state.pageSize
        };

        document.querySelectorAll('.qb-join-row').forEach(row => {
            const parsed = parseTableValue(row.querySelector('.join-table')?.value);
            if (!parsed) return;
            request.joins.push({
                joinType: row.querySelector('.join-type').value,
                schema: parsed.schema, table: parsed.table,
                leftColumn: row.querySelector('.join-left-col').value,
                rightColumn: row.querySelector('.join-right-col').value
            });
        });

        document.querySelectorAll('#whereContainer .qb-where-row').forEach(row => {
            const col = row.querySelector('.where-column').value;
            if (!col) return;
            const activeMode = row.querySelector('.eb-btn-when[aria-pressed="true"]')?.dataset.mode || null;
            request.whereConditions.push({
                column: col, operator: row.querySelector('.where-operator').value,
                value: activeMode ? null : (row.querySelector('.where-value').value || null),
                connector: row.querySelector('.where-connector').value,
                valueKind: activeMode || 'Literal'
            });
        });

        document.querySelectorAll('.qb-groupby-row').forEach(row => {
            const val = row.querySelector('.groupby-column').value;
            if (!val) return;
            const p = val.split('.');
            request.groupByColumns.push({ schema: p[0], table: p[1], column: p[2] });
        });

        document.querySelectorAll('.qb-aggregate-row').forEach(row => {
            const colVal = row.querySelector('.agg-column').value;
            if (!colVal) return;
            const fn = row.querySelector('.agg-function').value;
            const alias = row.querySelector('.agg-alias').value;
            if (colVal === '*') {
                request.aggregateColumns.push({ function: fn, schema: '', table: '', column: '*', alias });
            } else {
                const p = colVal.split('.');
                request.aggregateColumns.push({ function: fn, schema: p[0], table: p[1], column: p[2], alias });
            }
        });

        document.querySelectorAll('.qb-orderby-row').forEach(row => {
            const col = row.querySelector('.orderby-column').value;
            if (!col) return;
            request.orderByColumns.push({ column: col, direction: row.querySelector('.orderby-dir').value });
        });

        if (!request.groupByColumns.length && !request.aggregateColumns.length) {
            const selectAllChecked = document.getElementById('selectAllCols')?.checked;
            if (!selectAllChecked) {
                document.querySelectorAll('.col-check:checked').forEach(cb => {
                    request.selectedColumns.push({ schema: cb.dataset.schema, table: cb.dataset.table, column: cb.dataset.column });
                });
            }
        }

        loadingSpinner.classList.remove('d-none');
        executeBtn.disabled = true;

        try {
            const resp = await fetch('/EntityBuilder/ExecuteQuery', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
                body: JSON.stringify(request)
            });
            const data = await resp.json();
            lastQueryParameters = data.parameters || {};
            lastQueryDefinition = request;
            renderResults(data);
        } catch (err) {
            lastQueryParameters = null;
            lastQueryDefinition = null;
            renderResults({ isSuccess: false, errorMessage: err.message });
        } finally {
            loadingSpinner.classList.add('d-none');
            executeBtn.disabled = false;
        }
    }

    // ========== SEND REPORT TO EMAIL ==========
    const reportFeedback = document.getElementById('reportFeedback');

    function showReportFeedback(message, isError) {
        reportFeedback.textContent = message;
        reportFeedback.style.background = isError ? '#FEE2E2' : '#D1FAE5';
        reportFeedback.style.color = isError ? '#991B1B' : '#065F46';
        reportFeedback.classList.remove('d-none');
        if (!isError) setTimeout(() => reportFeedback.classList.add('d-none'), 5000);
    }

    // ========== SEND REPORT MODAL ==========
    const sendReportModal = document.getElementById('sendReportModal');
    const sendReportModalInstance = sendReportModal ? new bootstrap.Modal(sendReportModal) : null;
    const sendToLoginCheckbox = document.getElementById('sendToLoginEmail');
    const customEmailGroup = document.getElementById('customEmailGroup');
    const customRecipientEmail = document.getElementById('customRecipientEmail');
    const reportSubjectInput = document.getElementById('reportSubject');

    sendToLoginCheckbox?.addEventListener('change', function () {
        if (this.checked) {
            customEmailGroup.classList.add('d-none');
            customRecipientEmail.value = '';
        } else {
            customEmailGroup.classList.remove('d-none');
            customRecipientEmail.focus();
        }
    });

    sendReportBtn?.addEventListener('click', function () {
        reportFeedback.classList.add('d-none');
        const sqlPreview = document.getElementById('sqlPreview');
        const rawSql = sqlPreview?.textContent?.trim();

        if (!rawSql) {
            showReportFeedback('Please execute a query first to generate SQL.', true);
            return;
        }

        // Reset modal to defaults
        reportSubjectInput.value = 'Entity Builder Report';
        sendToLoginCheckbox.checked = true;
        customEmailGroup.classList.add('d-none');
        customRecipientEmail.value = '';

        sendReportModalInstance.show();
    });

    document.getElementById('confirmSendReport')?.addEventListener('click', async function () {
        const sqlPreview = document.getElementById('sqlPreview');
        const rawSql = sqlPreview?.textContent?.trim();
        const sql = rawSql.replace(/\s+OFFSET\s+\d+\s+ROWS\s+FETCH\s+NEXT\s+\d+\s+ROWS\s+ONLY/gi, '');

        const subject = reportSubjectInput.value.trim();
        if (!subject) {
            reportSubjectInput.classList.add('is-invalid');
            return;
        }
        reportSubjectInput.classList.remove('is-invalid');

        const useLoginEmail = sendToLoginCheckbox.checked;
        let recipientEmail = null;

        if (!useLoginEmail) {
            recipientEmail = customRecipientEmail.value.trim();
            if (!recipientEmail) {
                customRecipientEmail.classList.add('is-invalid');
                return;
            }
            customRecipientEmail.classList.remove('is-invalid');
        }

        sendReportModalInstance.hide();
        sendReportBtn.disabled = true;
        reportSpinner.classList.remove('d-none');

        try {
            const body = {
                sql,
                subject,
                dapperTemplateValues: lastQueryParameters || {}
            };
            if (recipientEmail) body.recipientEmail = recipientEmail;

            const resp = await fetch('/EntityBuilder/SendReportEmail', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
                body: JSON.stringify(body)
            });

            const result = await resp.json();

            if (resp.ok) {
                showReportFeedback('Report sent successfully!', false);
            } else {
                showReportFeedback(result.message || 'Failed to send report.', true);
            }
        } catch (err) {
            showReportFeedback('Error sending report: ' + err.message, true);
        } finally {
            reportSpinner.classList.add('d-none');
            sendReportBtn.disabled = false;
        }
    });

    // ========== RENDER RESULTS ==========
    function renderResults(result) {
        // Show generated SQL
        const sqlSection = document.getElementById('sqlSection');
        const sqlPreview = document.getElementById('sqlPreview');
        if (result.generatedSql) {
            sqlSection.classList.remove('d-none');
            sqlPreview.textContent = formatSql(result.generatedSql);
        } else {
            sqlSection.classList.add('d-none');
        }

        // Reset to grid view
        switchView('grid');

        resultsSection.classList.remove('d-none');
        const header = document.getElementById('resultsHeader');
        const errorAlert = document.getElementById('errorAlert');
        const noDataAlert = document.getElementById('noDataAlert');
        const gridContainer = document.getElementById('dataGridContainer');
        const thead = document.getElementById('resultsTableHead');
        const tbody = document.getElementById('resultsTableBody');
        const pagination = document.getElementById('paginationContainer');

        errorAlert.classList.add('d-none');
        noDataAlert.classList.add('d-none');
        gridContainer.classList.remove('d-none');
        thead.innerHTML = ''; tbody.innerHTML = ''; pagination.innerHTML = '';

        if (!result.isSuccess) {
            header.innerHTML = '<strong><i class="bi bi-exclamation-triangle me-1"></i> Error</strong>';
            errorAlert.innerHTML = `<i class="bi bi-exclamation-triangle-fill me-2"></i>${escapeHtml(result.errorMessage)}`;
            errorAlert.classList.remove('d-none');
            gridContainer.classList.add('d-none');
            viewModeTabs?.classList.add('d-none');
            if (barChartInstance) { barChartInstance.destroy(); barChartInstance = null; }
            if (pieChartInstance) { pieChartInstance.destroy(); pieChartInstance = null; }
            lastResultData = null;
            return;
        }

        header.innerHTML = `<strong><i class="bi bi-table me-1"></i> ${result.totalRows.toLocaleString()} total rows</strong>
            <span class="text-muted">Page ${result.currentPage} of ${result.totalPages} &bull; ${result.executionTimeMs}ms</span>`;

        if (!result.rows.length) {
            noDataAlert.classList.remove('d-none');
            gridContainer.classList.add('d-none');
            viewModeTabs?.classList.add('d-none');
            lastResultData = null;
            return;
        }

        // Store result for charts and show tabs
        lastResultData = result;
        viewModeTabs?.classList.remove('d-none');

        // Populate chart column selectors
        if (chartLabelSelect && chartValueSelect) {
            const colOpts = result.columns.map(c => `<option value="${escapeHtml(c)}">${escapeHtml(c)}</option>`).join('');
            chartLabelSelect.innerHTML = '<option value="">-- Select column --</option>' + colOpts;
            chartValueSelect.innerHTML = '<option value="">-- Select column --</option>' + colOpts;
            if (result.columns.length >= 2) {
                chartLabelSelect.value = result.columns[0];
                let valueIdx = 1;
                for (let i = 1; i < result.columns.length; i++) {
                    if (result.rows.length > 0 && typeof result.rows[0][result.columns[i]] === 'number') { valueIdx = i; break; }
                }
                chartValueSelect.value = result.columns[valueIdx];
            }
        }

        let headHtml = '<tr>';
        result.columns.forEach(col => { headHtml += `<th>${escapeHtml(col)}</th>`; });
        thead.innerHTML = headHtml + '</tr>';

        let bodyHtml = '';
        result.rows.forEach(row => {
            bodyHtml += '<tr>';
            result.columns.forEach(col => {
                const val = row[col];
                bodyHtml += `<td>${val === null ? '<span class="eb-null-value">NULL</span>' : escapeHtml(String(val))}</td>`;
            });
            bodyHtml += '</tr>';
        });
        tbody.innerHTML = bodyHtml;

        if (result.totalPages > 1) renderPagination(pagination, result.currentPage, result.totalPages);
        resultsSection.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    function renderPagination(container, cur, total) {
        let html = '<nav><ul class="eb-pagination">';
        html += `<li class="page-item ${cur <= 1 ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${cur - 1}"><i class="bi bi-chevron-left"></i> Prev</a></li>`;

        let start = Math.max(1, cur - 2), end = Math.min(total, start + 4);
        start = Math.max(1, end - 4);

        if (start > 1) {
            html += `<li class="page-item"><a class="page-link" href="#" data-page="1">1</a></li>`;
            if (start > 2) html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
        }
        for (let i = start; i <= end; i++)
            html += `<li class="page-item ${i === cur ? 'active' : ''}"><a class="page-link" href="#" data-page="${i}">${i}</a></li>`;
        if (end < total) {
            if (end < total - 1) html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
            html += `<li class="page-item"><a class="page-link" href="#" data-page="${total}">${total}</a></li>`;
        }
        html += `<li class="page-item ${cur >= total ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${cur + 1}">Next <i class="bi bi-chevron-right"></i></a></li>`;
        html += '</ul></nav>';
        container.innerHTML = html;

        container.querySelectorAll('.page-link[data-page]').forEach(link => {
            link.addEventListener('click', function (e) {
                e.preventDefault();
                const page = parseInt(this.dataset.page);
                if (page >= 1 && page <= total && page !== cur) { state.currentPage = page; executeQuery(); }
            });
        });
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function formatSql(sql) {
        const keywords = ['SELECT', 'FROM', 'INNER JOIN', 'LEFT JOIN', 'RIGHT JOIN', 'FULL OUTER JOIN', 'ON', 'WHERE', 'AND', 'OR', 'GROUP BY', 'ORDER BY', 'OFFSET', 'FETCH NEXT'];
        let formatted = sql.trim();
        keywords.forEach(kw => {
            formatted = formatted.replace(new RegExp('\\s+' + kw.replace(/\s+/g, '\\s+') + '\\s', 'gi'), '\n' + kw + ' ');
        });
        return formatted.trim();
    }

    // Copy SQL button
    document.getElementById('copySqlBtn')?.addEventListener('click', function () {
        const sql = document.getElementById('sqlPreview')?.textContent;
        if (sql) {
            navigator.clipboard.writeText(sql).then(() => {
                this.innerHTML = '<i class="bi bi-check-lg"></i> Copied!';
                setTimeout(() => { this.innerHTML = '<i class="bi bi-clipboard"></i> Copy'; }, 2000);
            });
        }
    });

    // ========== CHART FUNCTIONS ==========
    function getChartColors(count) {
        const palette = [
            '#DC2626', '#B91C1C', '#EF4444', '#F87171', '#991B1B',
            '#7F1D1D', '#FCA5A5', '#374151', '#6B7280', '#9CA3AF',
            '#1F2937', '#D1D5DB', '#4B5563', '#F59E0B', '#10B981'
        ];
        const colors = [];
        for (let i = 0; i < count; i++) colors.push(palette[i % palette.length]);
        return colors;
    }

    function switchView(view) {
        currentView = view;
        viewModeTabs?.querySelectorAll('.eb-view-tab').forEach(t =>
            t.classList.toggle('active', t.dataset.view === view));

        const gridContainer = document.getElementById('dataGridContainer');
        const pagination = document.getElementById('paginationContainer');

        gridContainer?.classList.toggle('d-none', view !== 'grid');
        pagination?.classList.toggle('d-none', view !== 'grid');
        barChartContainer?.classList.toggle('d-none', view !== 'bar');
        pieChartContainer?.classList.toggle('d-none', view !== 'pie');
        chartConfigPanel?.classList.toggle('d-none', view === 'grid');

        if (view !== 'grid' && lastResultData) renderChart(view);
    }

    function renderChart(type) {
        if (!lastResultData || !lastResultData.rows.length) return;

        const labelCol = chartLabelSelect?.value;
        const valueCol = chartValueSelect?.value;
        if (!labelCol || !valueCol) return;

        const rows = lastResultData.rows;
        const labels = rows.map(r => r[labelCol] !== null ? String(r[labelCol]) : 'NULL');
        const values = rows.map(r => {
            const v = parseFloat(r[valueCol]);
            return isNaN(v) ? 0 : v;
        });

        const colors = getChartColors(labels.length);

        if (type === 'bar') {
            if (barChartInstance) barChartInstance.destroy();
            barChartInstance = new Chart(barChartCanvas.getContext('2d'), {
                type: 'bar',
                data: {
                    labels,
                    datasets: [{
                        label: valueCol,
                        data: values,
                        backgroundColor: 'rgba(220, 38, 38, 0.8)',
                        borderColor: '#DC2626',
                        borderWidth: 1,
                        borderRadius: 4,
                        hoverBackgroundColor: '#B91C1C'
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { labels: { font: { family: "'Inter', sans-serif", weight: '600' }, color: '#374151' } },
                        tooltip: { backgroundColor: '#1F2937', titleFont: { family: "'Inter', sans-serif", weight: '700' }, bodyFont: { family: "'Inter', sans-serif" }, cornerRadius: 8, padding: 10 }
                    },
                    scales: {
                        x: { ticks: { font: { family: "'Inter', sans-serif", size: 11 }, color: '#6B7280', maxRotation: 45 }, grid: { color: '#F3F4F6' } },
                        y: { beginAtZero: true, ticks: { font: { family: "'Inter', sans-serif", size: 11 }, color: '#6B7280' }, grid: { color: '#F3F4F6' } }
                    }
                }
            });
        } else if (type === 'pie') {
            if (pieChartInstance) pieChartInstance.destroy();
            pieChartInstance = new Chart(pieChartCanvas.getContext('2d'), {
                type: 'pie',
                data: {
                    labels,
                    datasets: [{
                        data: values,
                        backgroundColor: colors,
                        borderColor: '#FFFFFF',
                        borderWidth: 2,
                        hoverOffset: 8
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { position: 'right', labels: { font: { family: "'Inter', sans-serif", size: 12, weight: '500' }, color: '#374151', padding: 12, usePointStyle: true, pointStyleWidth: 10 } },
                        tooltip: { backgroundColor: '#1F2937', titleFont: { family: "'Inter', sans-serif", weight: '700' }, bodyFont: { family: "'Inter', sans-serif" }, cornerRadius: 8, padding: 10 }
                    }
                }
            });
        }
    }

    // View tab switching
    viewModeTabs?.addEventListener('click', function (e) {
        const tab = e.target.closest('.eb-view-tab');
        if (!tab || tab.dataset.view === currentView) return;
        switchView(tab.dataset.view);
    });

    // Re-render chart when column selection changes
    chartLabelSelect?.addEventListener('change', () => { if (currentView !== 'grid' && lastResultData) renderChart(currentView); });
    chartValueSelect?.addEventListener('change', () => { if (currentView !== 'grid' && lastResultData) renderChart(currentView); });

    // ========== SCHEDULE REPORT ==========
    const scheduleReportModal = document.getElementById('scheduleReportModal');
    const scheduleReportModalInstance = scheduleReportModal ? new bootstrap.Modal(scheduleReportModal) : null;
    scheduleReportModal?.addEventListener('hidden.bs.modal', () => { editingReportId = null; });
    const scheduleFrequencySelect = document.getElementById('scheduleFrequency');
    const scheduleDateGroup = document.getElementById('scheduleDateGroup');
    const scheduleDayOfWeekGroup = document.getElementById('scheduleDayOfWeekGroup');
    const scheduleDayOfMonthGroup = document.getElementById('scheduleDayOfMonthGroup');
    const scheduleSendToLoginCheckbox = document.getElementById('scheduleSendToLoginEmail');
    const scheduleCustomEmailGroup = document.getElementById('scheduleCustomEmailGroup');
    const scheduleCustomEmail = document.getElementById('scheduleCustomEmail');
    const scheduleSubjectInput = document.getElementById('scheduleSubject');

    const frequencyLabels = ['Once', 'Daily', 'Weekly', 'Monthly'];
    const statusLabels = ['Queued', 'Sent', 'Failed', 'Cancelled'];
    const statusColors = { 0: '#059669', 1: '#2563EB', 2: '#DC2626', 3: '#6B7280' };

    // When set, the schedule modal is in "edit" mode and Save PUTs instead of creating a new report.
    let editingReportId = null;
    const scheduleModalTitle = document.getElementById('scheduleReportModalLabel');
    const confirmScheduleBtn = document.getElementById('confirmScheduleReport');

    const scheduleQueryEditor = document.getElementById('scheduleQueryEditor');
    const scheduleQuerySummary = document.getElementById('scheduleQuerySummary');
    const scheduleWhereContainer = document.getElementById('scheduleWhereContainer');
    const scheduleAddWhereBtn = document.getElementById('scheduleAddWhere');

    // Holds the QueryBuilderRequest we're editing. WHERE conditions inside get replaced from the
    // modal's WHERE rows on save; the rest of the definition (table / joins / selects / etc.) is
    // preserved as-is and re-sent to the server, which re-generates SQL from it.
    let editingQueryDefinition = null;

    function setScheduleModalMode(mode) {
        // mode: 'create' | 'edit'
        const editing = mode === 'edit';
        if (scheduleModalTitle) {
            scheduleModalTitle.innerHTML = editing
                ? '<i class="bi bi-pencil-square me-2"></i>Edit Scheduled Report'
                : '<i class="bi bi-clock-fill me-2"></i>Schedule Report';
        }
        if (confirmScheduleBtn) {
            confirmScheduleBtn.innerHTML = editing
                ? '<i class="bi bi-check-lg me-1"></i> Save Changes'
                : '<i class="bi bi-clock-fill me-1"></i> Schedule Report';
        }
        // The query editor (summary + WHERE dropdowns) only makes sense when editing an existing report.
        scheduleQueryEditor?.classList.toggle('d-none', !editing);
        if (!editing) {
            editingQueryDefinition = null;
            if (scheduleQuerySummary) scheduleQuerySummary.innerHTML = '';
            if (scheduleWhereContainer) scheduleWhereContainer.innerHTML = '';
        }
    }

    // Column options limited to the tables actually referenced by the query being edited.
    // Assumes state.columns has already been hydrated by ensureColumnsForDefinition.
    function buildScheduleColumnOptions(qdef) {
        const seen = new Set();
        const tables = [{ schema: qdef.schema, table: qdef.table }];
        (qdef.joins || []).forEach(j => tables.push({ schema: j.schema, table: j.table }));
        let html = '';
        tables.forEach(t => {
            const key = `${t.schema}.${t.table}`;
            if (seen.has(key)) return;
            seen.add(key);
            const cols = state.columns[key] || [];
            if (!cols.length) return;
            html += `<optgroup label="${escapeHtml(t.table)}">`;
            cols.forEach(c => {
                html += `<option value="${t.schema}.${t.table}.${c.columnName}">[${escapeHtml(t.table)}].${escapeHtml(c.columnName)}</option>`;
            });
            html += '</optgroup>';
        });
        return html;
    }

    async function ensureColumnsForDefinition(qdef) {
        const tables = [{ schema: qdef.schema, table: qdef.table }];
        (qdef.joins || []).forEach(j => tables.push({ schema: j.schema, table: j.table }));
        const uniq = new Map();
        tables.forEach(t => uniq.set(`${t.schema}.${t.table}`, t));
        // Sequentially to keep it simple; there are usually only a handful of tables in one query.
        for (const t of uniq.values()) {
            const key = `${t.schema}.${t.table}`;
            if (!state.columns[key]) await fetchColumns(t.schema, t.table);
        }
    }

    function addScheduleWhereRow(cond, columnOptions, isFirst) {
        const row = document.createElement('div');
        row.className = 'qb-where-row';
        row.innerHTML = `
            <select class="sw-connector" ${isFirst ? 'style="visibility:hidden"' : ''}>
                <option value="AND">AND</option><option value="OR">OR</option>
            </select>
            <select class="sw-column"><option value="">-- Column --</option>${columnOptions}</select>
            <select class="sw-operator">
                <option value="=">=</option><option value="!=">!=</option>
                <option value=">">&gt;</option><option value="<">&lt;</option>
                <option value=">=">&gt;=</option><option value="<=">&lt;=</option>
                <option value="LIKE">LIKE</option><option value="IN">IN</option>
                <option value="IS NULL">IS NULL</option><option value="IS NOT NULL">IS NOT NULL</option>
            </select>
            <input type="text" class="sw-value" placeholder="Value">
            ${VALUE_KIND_BUTTONS_HTML}
            <button type="button" class="eb-btn-remove" title="Remove"><i class="bi bi-x"></i></button>`;
        scheduleWhereContainer.appendChild(row);

        const opSel = row.querySelector('.sw-operator');
        const valInput = row.querySelector('.sw-value');
        const toggles = wireValueKindToggles(row, opSel, valInput);

        row.querySelector('.eb-btn-remove').addEventListener('click', function () {
            row.remove();
            const first = scheduleWhereContainer.querySelector('.qb-where-row');
            if (first) first.querySelector('.sw-connector').style.visibility = 'hidden';
        });

        // Preload values from the stored condition.
        if (cond) {
            row.querySelector('.sw-connector').value = cond.connector || 'AND';
            row.querySelector('.sw-column').value = cond.column || '';
            opSel.value = cond.operator || '=';
            if (cond.valueKind === 'Now' || cond.valueKind === 'Today') {
                toggles.setMode(cond.valueKind);
            } else if (cond.value != null) {
                valInput.value = cond.value;
            }
            opSel.dispatchEvent(new Event('change'));
            // If the operator dispatch cleared the toggle (e.g. IN / IS NULL), re-apply the stored mode.
            if (cond.valueKind === 'Now' || cond.valueKind === 'Today') toggles.setMode(cond.valueKind);
        }

        initSearchableSelects(row);
        return row;
    }

    async function populateScheduleQueryEditor(qdef) {
        editingQueryDefinition = qdef;
        if (!qdef) {
            scheduleQuerySummary.innerHTML = '<em class="text-danger">This report was scheduled before edit support existed. Cancel it and re-schedule from the query builder.</em>';
            scheduleWhereContainer.innerHTML = '';
            scheduleAddWhereBtn.disabled = true;
            return;
        }
        scheduleAddWhereBtn.disabled = false;

        const parts = [];
        parts.push(`<div><strong>Table:</strong> [${escapeHtml(qdef.schema)}].[${escapeHtml(qdef.table)}]</div>`);
        if (qdef.joins?.length) {
            parts.push('<div><strong>Joins:</strong> ' + qdef.joins.map(j => `${escapeHtml(j.joinType)} [${escapeHtml(j.schema)}].[${escapeHtml(j.table)}]`).join(', ') + '</div>');
        }
        if (qdef.selectedColumns?.length) {
            parts.push('<div><strong>Columns:</strong> ' + qdef.selectedColumns.map(c => escapeHtml(c.column)).join(', ') + '</div>');
        }
        if (qdef.groupByColumns?.length) {
            parts.push('<div><strong>Group by:</strong> ' + qdef.groupByColumns.map(g => escapeHtml(g.column)).join(', ') + '</div>');
        }
        if (qdef.aggregateColumns?.length) {
            parts.push('<div><strong>Aggregates:</strong> ' + qdef.aggregateColumns.map(a => `${escapeHtml(a.function)}(${escapeHtml(a.column)})`).join(', ') + '</div>');
        }
        if (qdef.orderByColumns?.length) {
            parts.push('<div><strong>Order by:</strong> ' + qdef.orderByColumns.map(o => `${escapeHtml(o.column)} ${escapeHtml(o.direction || 'ASC')}`).join(', ') + '</div>');
        }
        scheduleQuerySummary.innerHTML = parts.join('');

        await ensureColumnsForDefinition(qdef);
        const columnOptions = buildScheduleColumnOptions(qdef);

        scheduleWhereContainer.innerHTML = '';
        const conditions = qdef.whereConditions || [];
        conditions.forEach((c, i) => addScheduleWhereRow(c, columnOptions, i === 0));
    }

    // Wire the "add condition" button for the modal (fires against the currently-loaded definition).
    scheduleAddWhereBtn?.addEventListener('click', function () {
        if (!editingQueryDefinition) return;
        const columnOptions = buildScheduleColumnOptions(editingQueryDefinition);
        const isFirst = scheduleWhereContainer.querySelectorAll('.qb-where-row').length === 0;
        addScheduleWhereRow(null, columnOptions, isFirst);
    });

    function collectScheduleWhereConditions() {
        const out = [];
        scheduleWhereContainer.querySelectorAll('.qb-where-row').forEach(row => {
            const col = row.querySelector('.sw-column').value;
            if (!col) return;
            const activeMode = row.querySelector('.eb-btn-when[aria-pressed="true"]')?.dataset.mode || null;
            out.push({
                column: col,
                operator: row.querySelector('.sw-operator').value,
                value: activeMode ? null : (row.querySelector('.sw-value').value || null),
                connector: row.querySelector('.sw-connector').value,
                valueKind: activeMode || 'Literal'
            });
        });
        return out;
    }

    function populateScheduleModalFromReport(r) {
        scheduleSubjectInput.value = r.subject || 'Entity Builder Report';

        // If the recipient matches the logged-in user email we can't know that server-side without another lookup,
        // so always show it in the custom-email field for clarity when editing.
        scheduleSendToLoginCheckbox.checked = false;
        scheduleCustomEmailGroup.classList.remove('d-none');
        scheduleCustomEmail.value = r.recipientEmail || '';

        scheduleFrequencySelect.value = String(r.frequency ?? 0);
        scheduleFrequencySelect.dispatchEvent(new Event('change'));

        document.getElementById('scheduleTime').value = r.scheduledTime || '08:00';
        document.getElementById('scheduleDate').value = r.scheduledDate || '';
        if (r.dayOfWeek != null) document.getElementById('scheduleDayOfWeek').value = String(r.dayOfWeek);
        if (r.dayOfMonth != null) document.getElementById('scheduleDayOfMonth').value = String(r.dayOfMonth);
    }

    // Toggle conditional fields based on frequency
    scheduleFrequencySelect?.addEventListener('change', function () {
        const freq = parseInt(this.value);
        scheduleDateGroup.classList.toggle('d-none', freq !== 0);
        scheduleDayOfWeekGroup.classList.toggle('d-none', freq !== 2);
        scheduleDayOfMonthGroup.classList.toggle('d-none', freq !== 3);
    });

    scheduleSendToLoginCheckbox?.addEventListener('change', function () {
        if (this.checked) {
            scheduleCustomEmailGroup.classList.add('d-none');
            scheduleCustomEmail.value = '';
        } else {
            scheduleCustomEmailGroup.classList.remove('d-none');
            scheduleCustomEmail.focus();
        }
    });

    // Open schedule modal (create mode)
    document.getElementById('scheduleReport')?.addEventListener('click', function () {
        reportFeedback.classList.add('d-none');
        const sqlPreview = document.getElementById('sqlPreview');
        const rawSql = sqlPreview?.textContent?.trim();

        if (!rawSql) {
            showReportFeedback('Please execute a query first to generate SQL.', true);
            return;
        }

        editingReportId = null;
        setScheduleModalMode('create');

        // Reset modal
        scheduleSubjectInput.value = 'Entity Builder Report';
        scheduleSendToLoginCheckbox.checked = true;
        scheduleCustomEmailGroup.classList.add('d-none');
        scheduleCustomEmail.value = '';
        scheduleFrequencySelect.value = '0';
        scheduleDateGroup.classList.remove('d-none');
        scheduleDayOfWeekGroup.classList.add('d-none');
        scheduleDayOfMonthGroup.classList.add('d-none');
        document.getElementById('scheduleTime').value = '08:00';
        document.getElementById('scheduleDate').value = '';
        document.getElementById('scheduleDayOfMonth').value = '1';

        scheduleReportModalInstance.show();
    });

    // Confirm schedule (create or edit)
    document.getElementById('confirmScheduleReport')?.addEventListener('click', async function () {
        const isEdit = !!editingReportId;

        const subject = scheduleSubjectInput.value.trim();
        if (!subject) {
            scheduleSubjectInput.classList.add('is-invalid');
            return;
        }
        scheduleSubjectInput.classList.remove('is-invalid');

        const useLoginEmail = scheduleSendToLoginCheckbox.checked;
        let recipientEmail = null;
        if (!useLoginEmail) {
            recipientEmail = scheduleCustomEmail.value.trim();
            if (!recipientEmail) {
                scheduleCustomEmail.classList.add('is-invalid');
                return;
            }
            scheduleCustomEmail.classList.remove('is-invalid');
        }

        const frequency = parseInt(scheduleFrequencySelect.value);
        const scheduledTime = document.getElementById('scheduleTime').value || '08:00';
        const scheduledDate = document.getElementById('scheduleDate').value || null;
        const dayOfWeek = frequency === 2 ? parseInt(document.getElementById('scheduleDayOfWeek').value) : null;
        const dayOfMonth = frequency === 3 ? parseInt(document.getElementById('scheduleDayOfMonth').value) : null;
        const utcOffsetMinutes = new Date().getTimezoneOffset();

        // Edit path — server rebuilds SQL from the (modified) structured definition. Client never posts raw SQL.
        if (isEdit) {
            let queryDefinition = null;
            if (editingQueryDefinition) {
                queryDefinition = { ...editingQueryDefinition, whereConditions: collectScheduleWhereConditions() };
            }
            scheduleReportModalInstance.hide();
            try {
                const body = { subject, recipientEmail, queryDefinition, frequency, scheduledTime, scheduledDate, dayOfWeek, dayOfMonth, utcOffsetMinutes };
                const resp = await fetch(`/EntityBuilder/UpdateScheduledReport/${editingReportId}`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
                    body: JSON.stringify(body)
                });
                const result = await resp.json();
                if (resp.ok) {
                    showReportFeedback('Scheduled report updated.', false);
                    loadScheduledReports();
                } else {
                    showReportFeedback(result.message || 'Failed to update report.', true);
                }
            } catch (err) {
                showReportFeedback('Error updating report: ' + err.message, true);
            } finally {
                editingReportId = null;
                editingQueryDefinition = null;
            }
            return;
        }

        // Create path
        const sqlPreview = document.getElementById('sqlPreview');
        const rawSql = sqlPreview?.textContent?.trim();
        const sql = rawSql.replace(/\s+OFFSET\s+\d+\s+ROWS\s+FETCH\s+NEXT\s+\d+\s+ROWS\s+ONLY/gi, '');

        scheduleReportModalInstance.hide();

        try {
            const body = {
                // Preferred: structured definition — server rebuilds SQL from it (no client SQL trusted).
                queryDefinition: lastQueryDefinition,
                // Legacy fallback if the server is older or the definition isn't available.
                sql,
                subject,
                dapperTemplateValues: lastQueryParameters || {},
                frequency,
                scheduledTime,
                scheduledDate,
                dayOfWeek,
                dayOfMonth,
                utcOffsetMinutes
            };
            if (recipientEmail) body.recipientEmail = recipientEmail;

            const resp = await fetch('/EntityBuilder/ScheduleReport', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiForgeryToken() },
                body: JSON.stringify(body)
            });

            const result = await resp.json();
            if (resp.ok) {
                showReportFeedback('Report scheduled successfully!', false);
                loadScheduledReports();
            } else {
                showReportFeedback(result.message || 'Failed to schedule report.', true);
            }
        } catch (err) {
            showReportFeedback('Error scheduling report: ' + err.message, true);
        }
    });

    // Load scheduled reports
    async function loadScheduledReports() {
        const emptyMsg = document.getElementById('scheduledReportsEmpty');
        const tableContainer = document.getElementById('scheduledReportsTableContainer');
        const tbody = document.getElementById('scheduledReportsBody');

        try {
            const resp = await fetch('/EntityBuilder/ScheduledReports');
            if (!resp.ok) return;

            const reports = await resp.json();
            const activeReports = reports.filter(r => r.status !== 3);

            if (activeReports.length === 0) {
                emptyMsg.style.display = '';
                tableContainer.style.display = 'none';
                return;
            }

            emptyMsg.style.display = 'none';
            tableContainer.style.display = '';

            tbody.innerHTML = activeReports.map(r => {
                const nextRun = new Date(r.nextRun).toLocaleString();
                const statusColor = statusColors[r.status] || '#6B7280';
                const statusLabel = statusLabels[r.status] || 'Unknown';
                const freqLabel = frequencyLabels[r.frequency] || 'Unknown';
                // 0=Queued, 1=Sent, 2=Failed
                const canEdit = r.status === 0 || r.status === 2;
                const canRerun = r.status === 1 || r.status === 2;
                const canCancel = r.status === 0 || r.status === 2;
                const actions = [
                    canEdit ? `<button class="btn btn-sm btn-outline-secondary edit-schedule-btn me-1" data-id="${r.id}" title="Edit"><i class="bi bi-pencil"></i></button>` : '',
                    canRerun ? `<button class="btn btn-sm btn-outline-primary rerun-schedule-btn me-1" data-id="${r.id}" title="Run again now"><i class="bi bi-arrow-clockwise"></i></button>` : '',
                    canCancel ? `<button class="btn btn-sm btn-outline-danger cancel-schedule-btn" data-id="${r.id}" title="Cancel"><i class="bi bi-x-circle"></i></button>` : ''
                ].join('');
                return `<tr>
                    <td style="padding:0.5rem 0.75rem;">${escapeHtml(r.subject)}</td>
                    <td style="padding:0.5rem 0.75rem;">${escapeHtml(r.recipientEmail)}</td>
                    <td style="padding:0.5rem 0.75rem;">${freqLabel}</td>
                    <td style="padding:0.5rem 0.75rem;">${nextRun}</td>
                    <td style="padding:0.5rem 0.75rem;"><span style="color:${statusColor};font-weight:600;">${statusLabel}</span></td>
                    <td style="padding:0.5rem 0.75rem;white-space:nowrap;">${actions}</td>
                </tr>`;
            }).join('');
        } catch (err) {
            console.error('Failed to load scheduled reports:', err);
        }
    }

    // Scheduled reports: cancel / rerun / edit
    document.getElementById('scheduledReportsBody')?.addEventListener('click', async function (e) {
        const cancelBtn = e.target.closest('.cancel-schedule-btn');
        const rerunBtn = e.target.closest('.rerun-schedule-btn');
        const editBtn = e.target.closest('.edit-schedule-btn');

        if (cancelBtn) {
            const id = cancelBtn.dataset.id;
            if (!confirm('Cancel this scheduled report?')) return;
            try {
                const resp = await fetch(`/EntityBuilder/CancelScheduledReport/${id}`, {
                    method: 'DELETE',
                    headers: { 'RequestVerificationToken': getAntiForgeryToken() }
                });
                if (resp.ok) {
                    showReportFeedback('Scheduled report cancelled.', false);
                    loadScheduledReports();
                } else {
                    const result = await resp.json();
                    showReportFeedback(result.message || 'Failed to cancel.', true);
                }
            } catch (err) {
                showReportFeedback('Error cancelling report: ' + err.message, true);
            }
            return;
        }

        if (rerunBtn) {
            const id = rerunBtn.dataset.id;
            if (!confirm('Run this report again now?')) return;
            try {
                const resp = await fetch(`/EntityBuilder/RerunScheduledReport/${id}`, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': getAntiForgeryToken() }
                });
                const result = await resp.json();
                if (resp.ok) {
                    showReportFeedback(result.message || 'Report re-queued.', false);
                    loadScheduledReports();
                } else {
                    showReportFeedback(result.message || 'Failed to rerun.', true);
                }
            } catch (err) {
                showReportFeedback('Error rerunning report: ' + err.message, true);
            }
            return;
        }

        if (editBtn) {
            const id = editBtn.dataset.id;
            try {
                const resp = await fetch(`/EntityBuilder/GetScheduledReport/${id}`);
                if (!resp.ok) {
                    const result = await resp.json().catch(() => ({}));
                    showReportFeedback(result.message || 'Failed to load report.', true);
                    return;
                }
                const report = await resp.json();
                editingReportId = id;
                setScheduleModalMode('edit');
                populateScheduleModalFromReport(report);
                await populateScheduleQueryEditor(report.queryDefinition);
                scheduleReportModalInstance.show();
            } catch (err) {
                showReportFeedback('Error loading report: ' + err.message, true);
            }
        }
    });

    document.getElementById('refreshScheduledReports')?.addEventListener('click', loadScheduledReports);

    // Load on page init
    loadScheduledReports();
});
