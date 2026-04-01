/**
 * Server admin shell bootstrap.
 * The management console opens directly into the service overview.
 */

import { $, api } from './utils.js';
import { enterApp } from './app.js';

export async function initSetup() {
  try {
    const status = await api('/status');
    const title = status?.configured
      ? `管理台 · ${status.projectName || '已配置项目'}`
      : '管理台 · 未配置项目';
    enterApp(title);
  } catch (error) {
    $('statusText').textContent = '连接失败: ' + error.message;
  }
}
