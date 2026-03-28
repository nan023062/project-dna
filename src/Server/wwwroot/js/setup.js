/**
 * 启动模块 — 直接进入主界面，不需要项目选择。
 * 每个 Server 实例绑定一个固定项目。
 */

import { $, api } from './utils.js';
import { enterApp } from './app.js';

export async function initSetup() {
  try {
    const config = await api('/config');
    if (config.configured) {
      enterApp(config.projectRoot);
    } else {
      $('statusText').textContent = '服务器未配置项目，请通过命令行 --project 参数启动。';
    }
  } catch (e) {
    $('statusText').textContent = '连接失败: ' + e.message;
  }
}

export function showSetup() {
  // no-op: 不再有项目选择页
}

export async function setProject() {
  // no-op
}

export function openProjectBrowser() {
  // no-op
}
