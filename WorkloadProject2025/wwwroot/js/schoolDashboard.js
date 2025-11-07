// Requires Chart.js to be loaded (include CDN in _Layout or page).
// This file exposes renderSchoolChart(labels, values, canvasId) and clearSchoolChart(canvasId).

window._schoolCharts = window._schoolCharts || {};

window.renderSchoolChart = function (labels, values, canvasId) {
    try {
        // ensure arrays
        labels = labels || [];
        values = values || [];

        const ctx = document.getElementById(canvasId);
        if (!ctx) {
            console.warn('renderSchoolChart: canvas element not found:', canvasId);
            return;
        }

        // destroy existing chart if present
        if (window._schoolCharts[canvasId]) {
            window._schoolCharts[canvasId].destroy();
            window._schoolCharts[canvasId] = null;
        }

        // generate color palette
        function randomColor(i) {
            const hue = (i * 137.508) % 360; // good distribution
            return `hsl(${hue}deg 70% 55%)`;
        }
        const backgroundColors = labels.map((_, i) => randomColor(i));

        const data = {
            labels: labels,
            datasets: [{
                data: values,
                backgroundColor: backgroundColors,
                borderColor: '#ffffff',
                borderWidth: 1
            }]
        };

        const config = {
            type: 'pie',
            data: data,
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { position: 'right' },
                    tooltip: { enabled: true }
                }
            }
        };

        // create chart
        window._schoolCharts[canvasId] = new Chart(ctx.getContext('2d'), config);
    } catch (err) {
        console.error('renderSchoolChart error', err);
    }
};

window.clearSchoolChart = function (canvasId) {
    try {
        if (window._schoolCharts && window._schoolCharts[canvasId]) {
            window._schoolCharts[canvasId].destroy();
            window._schoolCharts[canvasId] = null;
        }
    } catch (err) {
        console.error('clearSchoolChart error', err);
    }
};