/**
 * 项目选择模块
 * - 初始化检查配置
 * - 显示/隐藏 setup 页
 * - 设置项目路径
 * - 最近项目列表
 */

import { $, api } from './utils.js';
import { FolderBrowser } from './widgets/folder-browser.js';
import { enterApp } from './app.js';

const projectBrowser = new FolderBrowser({
  title: '选择项目文件夹',
  onConfirm: (path) => {
    $('projectInput').value = path;
    setProject();
  }
});

export async function initSetup() {
  try {
    const config = await api('/config');
    if (config.configured) {
      enterApp(config.projectRoot);
    } else {
      showSetup();
      if (config.recentProjects?.length > 0) {
        renderRecent(config.recentProjects);
      }
    }
  } catch (e) {
    $('statusText').textContent = '连接失败: ' + e.message;
  }
}

export function showSetup() {
  $('setupPage').style.display = 'flex';
  $('mainApp').classList.remove('active');
  const wrapper = $('appWrapper');
  if (wrapper) wrapper.classList.remove('active');
  api('/config').then(c => {
    if (c.recentProjects?.length > 0) renderRecent(c.recentProjects);
  }).catch(() => {});
}

function renderRecent(projects) {
  $('recentList').style.display = 'block';
  const container = $('recentItems');
  container.innerHTML = projects.map(p =>
    `<div class="recent-item" data-path="${p.path.replace(/"/g, '&quot;')}">
      <div class="name">${p.name}</div>
      <div class="rpath">${p.path}</div>
    </div>`
  ).join('');

  container.querySelectorAll('.recent-item').forEach(el => {
    el.addEventListener('click', () => {
      $('projectInput').value = el.dataset.path;
      setProject();
    });
  });
}

export async function setProject() {
  const path = $('projectInput').value.trim();
  if (!path) { $('setupError').textContent = '请输入项目路径'; return; }
  $('setupError').textContent = '';

  try {
    const data = await api('/config/project', {
      method: 'POST',
      body: { projectRoot: path }
    });
    if (data.success) {
      enterApp(data.projectRoot);
    } else {
      $('setupError').textContent = data.message;
    }
  } catch (e) {
    $('setupError').textContent = '请求失败: ' + e.message;
  }
}

export function openProjectBrowser() {
  const initial = $('projectInput').value.trim();
  projectBrowser.open(initial);
}
