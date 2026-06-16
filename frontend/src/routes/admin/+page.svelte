<script lang="ts">
  import Sidebar from '$lib/components/shared/Sidebar.svelte';
  import Header from '$lib/components/shared/Header.svelte';
</script>

<Sidebar activePage="Dashboard" />
<Header userName="Admin User" role="SYSTEM ROOT" />

<main class="dashboard-content">
  <div class="stats-grid">
    <div class="stat-card">
      <h4>WAREHOUSE CAPACITY</h4>
      <div class="value">84.2%</div>
      <div class="progress-bar"><div class="fill" style="width: 84.2%"></div></div>
      <p class="subtext">1,240 / 1,500 units used</p>
    </div>
    <div class="stat-card">
      <h4>TOTAL USERS</h4>
      <div class="value">42 <span class="trend pos">+3 this week</span></div>
    </div>
    <div class="stat-card">
      <h4>MONTHLY THROUGHPUT</h4>
      <div class="value">8.4k <span class="trend pos">12% growth</span></div>
    </div>
    <div class="stat-card system-health">
      <h4>SYSTEM HEALTH</h4>
      <div class="value text-white">Stable</div>
      <p class="text-white">All nodes operational</p>
    </div>
  </div>

  <div class="middle-grid">
    <div class="panel">
      <h3>Inventory Reports</h3>
      <div class="chart-placeholder">
        <div class="donut">1.2k<br/>Total Items</div>
        <div class="legend">
          <p>🟢 Electronics <strong>45%</strong></p>
          <p>🍏 Hardware <strong>30%</strong></p>
          <p>⚪ Logistics <strong>25%</strong></p>
        </div>
      </div>
    </div>
    <div class="panel">
      <h3>System Activity</h3>
      <ul class="activity-list">
        <li><strong>A. Smith</strong> added 45 units of SKU-8902<br/><span>2 mins ago • Warehouse A</span></li>
        <li><strong>System</strong> automatically backed up database<br/><span>45 mins ago • Global</span></li>
        <li><strong class="text-red">K. Jones</strong> flagged low stock: Item B-12<br/><span>1 hour ago • Warehouse B</span></li>
      </ul>
      <button class="btn-outline">View All Logs</button>
    </div>
  </div>

  <div class="panel mt-1">
    <div class="panel-header">
      <h3>User Management</h3>
      <div class="actions"><button class="btn-outline">Export</button><button class="btn-solid">+ Add New User</button></div>
    </div>
    <table class="data-table">
      <thead>
        <tr>
          <th>EMPLOYEE</th>
          <th>ROLE</th>
          <th>STATUS</th>
          <th>LAST ACTIVE</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td><strong>Alice Smith</strong><br/><span class="sub">alice.s@inventria.com</span></td>
          <td>Inventory Manager</td>
          <td><span class="badge active">ACTIVE</span></td>
          <td>Today, 09:42 AM</td>
        </tr>
        <tr>
          <td><strong>Bob Johnson</strong><br/><span class="sub">b.johnson@inventria.com</span></td>
          <td>Warehouse Staff</td>
          <td><span class="badge active">ACTIVE</span></td>
          <td>2h ago</td>
        </tr>
        <tr>
          <td><strong>Charlie Lee</strong><br/><span class="sub">clee@inventria.com</span></td>
          <td>Quality Control</td>
          <td><span class="badge leave">ON LEAVE</span></td>
          <td>3 days ago</td>
        </tr>
      </tbody>
    </table>
  </div>
</main>

<style>
  .dashboard-content { margin-left: 250px; padding: 2rem; background: #f8fafc; min-height: calc(100vh - 70px); }
  .stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1.5rem; margin-bottom: 1.5rem; }
  .stat-card { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; }
  .stat-card h4 { margin: 0 0 1rem 0; font-size: 0.75rem; color: #64748b; letter-spacing: 0.5px; }
  .stat-card .value { font-size: 2rem; font-weight: 700; color: #0f172a; margin-bottom: 0.5rem; display: flex; align-items: center; gap: 0.5rem; }
  .trend.pos { font-size: 0.85rem; color: #0b6b36; font-weight: 500; }
  .progress-bar { width: 100%; height: 6px; background: #e2e8f0; border-radius: 3px; margin-bottom: 0.5rem; overflow: hidden; }
  .progress-bar .fill { height: 100%; background: #0b6b36; }
  .stat-card .subtext { margin: 0; font-size: 0.8rem; color: #64748b; }
  .system-health { background: #0b6b36; border-color: #0b6b36; }
  .text-white { color: white !important; }
  .system-health h4 { color: #a7f3d0; }
  
  .middle-grid { display: grid; grid-template-columns: 2fr 1fr; gap: 1.5rem; }
  .panel { background: white; padding: 1.5rem; border-radius: 8px; border: 1px solid #e2e8f0; }
  .panel h3 { margin: 0 0 1.5rem 0; font-size: 1.1rem; color: #0f172a; }
  .chart-placeholder { display: flex; align-items: center; gap: 3rem; }
  .donut { width: 150px; height: 150px; border: 20px solid #0b6b36; border-radius: 20px; display: flex; align-items: center; justify-content: center; text-align: center; font-weight: bold; }
  
  .activity-list { list-style: none; padding: 0; margin: 0 0 1.5rem 0; }
  .activity-list li { margin-bottom: 1rem; font-size: 0.9rem; position: relative; padding-left: 1.5rem; }
  .activity-list li::before { content: ''; position: absolute; left: 0; top: 6px; width: 8px; height: 8px; border-radius: 50%; background: #cbd5e1; }
  .activity-list li:first-child::before { background: #0b6b36; }
  .activity-list li span { font-size: 0.75rem; color: #64748b; display: block; margin-top: 0.25rem; }
  .text-red { color: #ef4444; }
  
  .mt-1 { margin-top: 1.5rem; }
  .panel-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; }
  .btn-outline { background: white; border: 1px solid #cbd5e1; padding: 0.5rem 1rem; border-radius: 6px; font-weight: 500; cursor: pointer; }
  .btn-solid { background: #0b6b36; color: white; border: none; padding: 0.5rem 1rem; border-radius: 6px; font-weight: 500; cursor: pointer; margin-left: 0.5rem; }
  
  .data-table { width: 100%; border-collapse: collapse; text-align: left; }
  .data-table th { padding: 1rem; border-bottom: 2px solid #e2e8f0; color: #64748b; font-size: 0.75rem; letter-spacing: 0.5px; }
  .data-table td { padding: 1rem; border-bottom: 1px solid #e2e8f0; font-size: 0.9rem; }
  .data-table td .sub { font-size: 0.8rem; color: #64748b; }
  .badge { padding: 0.25rem 0.75rem; border-radius: 20px; font-size: 0.75rem; font-weight: 600; }
  .badge.active { background: #dcfce7; color: #166534; }
  .badge.leave { background: #e0e7ff; color: #3730a3; }
</style>