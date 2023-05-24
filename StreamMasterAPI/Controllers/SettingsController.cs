﻿using Microsoft.AspNetCore.Mvc;

using StreamMasterApplication.Common.Models;
using StreamMasterApplication.General.Queries;
using StreamMasterApplication.Settings;
using StreamMasterApplication.Settings.Commands;
using StreamMasterApplication.Settings.Queries;

using StreamMasterDomain.Dto;

namespace StreamMasterAPI.Controllers;

public class SettingsController : ApiControllerBase, ISettingController
{
    [HttpGet]
    [Route("[action]")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> GetIsSystemReady()
    {
        return await Mediator.Send(new GetIsSystemReadyRequest()).ConfigureAwait(false);
    }

    [HttpGet]
    [Route("[action]")]
    [ProducesResponseType(typeof(List<TaskQueueStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TaskQueueStatusDto>>> GetQueueStatus()
    {
        return await Mediator.Send(new GetQueueStatus()).ConfigureAwait(false);
    }

    [HttpGet]
    [ProducesResponseType(typeof(SettingDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettingDto>> GetSetting()
    {
        return await Mediator.Send(new GetSettings()).ConfigureAwait(false);
    }

    [HttpGet]
    [Route("[action]")]
    [ProducesResponseType(typeof(SystemStatus), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemStatus>> GetSystemStatus()
    {
        return await Mediator.Send(new GetSystemStatus()).ConfigureAwait(false);
    }

    [HttpPut]
    [Route("[action]")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SettingDto?>> UpdateSetting(UpdateSettingRequest command)
    {
        SettingDto data = await Mediator.Send(command).ConfigureAwait(false);
        return data == null ? NotFound() : NoContent();
    }
}
