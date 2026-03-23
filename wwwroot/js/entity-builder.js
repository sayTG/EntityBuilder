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
    const joinsContainer = document.getElementById('joinsContainer');
    const whereContainer = document.getElementById('whereContainer');
    const groupByContainer = document.getElementById('groupByContainer');
    const orderByContainer = document.getElementById('orderByContainer');
    const columnsContainer = document.getElementById('columnsContainer');
    const resultsSection = document.getElementById('resultsSection');
    const loadingSpinner = document.getElementById('loadingSpinner');

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

    function buildMainTableColumnOptions() {
        if (!state.mainTable) return '';
        const key = `${state.mainTable.schema}.${state.mainTable.table}`;
        const cols = state.columns[key] || [];
        if (!cols.length) return '';
        let html = `<optgroup label="${state.mainTable.table}">`;
        cols.forEach(c => {
            html += `<option value="${state.mainTable.schema}.${state.mainTable.table}.${c.columnName}">[${state.mainTable.table}].${c.columnName}</option>`;
        });
        html += '</optgroup>';
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
        const mainOpts = buildMainTableColumnOptions();
        document.querySelectorAll('.join-left-col').forEach(sel => {
            const cur = sel.value;
            sel.innerHTML = `<option value="">-- Left Column --</option>${mainOpts}`;
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

        const btns = [addJoinBtn, addWhereBtn, addGroupByBtn, addAggregateBtn, addOrderByBtn, executeBtn];
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
            <select class="join-left-col"><option value="">-- Left Column --</option>${buildMainTableColumnOptions()}</select>
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
            <button class="eb-btn-remove" title="Remove"><i class="bi bi-x"></i></button>`;
        whereContainer.appendChild(row);
        initSearchableSelects(row);

        const opSel = row.querySelector('.where-operator');
        const valInput = row.querySelector('.where-value');
        opSel.addEventListener('change', function () {
            valInput.style.display = (this.value === 'IS NULL' || this.value === 'IS NOT NULL') ? 'none' : '';
            if (valInput.style.display === 'none') valInput.value = '';
        });

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

        document.querySelectorAll('.qb-where-row').forEach(row => {
            const col = row.querySelector('.where-column').value;
            if (!col) return;
            request.whereConditions.push({
                column: col, operator: row.querySelector('.where-operator').value,
                value: row.querySelector('.where-value').value || null,
                connector: row.querySelector('.where-connector').value
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
            renderResults(await resp.json());
        } catch (err) {
            renderResults({ isSuccess: false, errorMessage: err.message });
        } finally {
            loadingSpinner.classList.add('d-none');
            executeBtn.disabled = false;
        }
    }

    // ========== RENDER RESULTS ==========
    function renderResults(result) {
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
            return;
        }

        header.innerHTML = `<strong><i class="bi bi-table me-1"></i> ${result.totalRows.toLocaleString()} total rows</strong>
            <span class="text-muted">Page ${result.currentPage} of ${result.totalPages} &bull; ${result.executionTimeMs}ms</span>`;

        if (!result.rows.length) { noDataAlert.classList.remove('d-none'); gridContainer.classList.add('d-none'); return; }

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
});
