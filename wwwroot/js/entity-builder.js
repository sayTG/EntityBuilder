document.addEventListener('DOMContentLoaded', function () {
    const sqlInput = document.getElementById('sqlInput');
    const queryForm = document.getElementById('queryForm');

    if (sqlInput && queryForm) {
        sqlInput.addEventListener('keydown', function (e) {
            if (e.ctrlKey && e.key === 'Enter') {
                e.preventDefault();
                queryForm.submit();
            }
        });
    }
});
