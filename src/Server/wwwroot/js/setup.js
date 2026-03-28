/**
 * 启动模块 — 直接进入主界面。
 * Server 是纯知识服务，无需项目配置。
 */

import { $, api } from './utils.js';
import { enterApp } from './app.js';

export async function initSetup() {
  try {
    const status = await api('/status');
    enterApp(`知识库 · ${status.moduleCount} 个模块`);
  } catch (e) {
    $('statusText').textContent = '连接失败: ' + e.message;
  }
}

export function showSetup() {}
export async function setProject() {}
export function openProjectBrowser() {}
