﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Data.Entity.Design.PluralizationServices;
using System.Web.Mvc;
using Coevery.Core.Services;
using Coevery.Entities.Services;
using Coevery.Entities.Settings;
using Coevery.Entities.ViewModels;
using Orchard;
using Orchard.ContentManagement;
using Orchard.ContentManagement.MetaData;
using Orchard.ContentManagement.MetaData.Models;
using Orchard.Localization;
using Orchard.Logging;
using Orchard.Utility.Extensions;
using Orchard.UI.Notify;
using Orchard.Core.Contents.Controllers;
using EditTypeViewModel = Coevery.Entities.ViewModels.EditTypeViewModel;
using IContentDefinitionEditorEvents = Orchard.ContentManagement.MetaData.IContentDefinitionEditorEvents;
using IContentDefinitionService = Coevery.Entities.Services.IContentDefinitionService;

namespace Coevery.Entities.Controllers {
    public class SystemAdminController : Controller, IUpdateModel {
        private readonly IContentDefinitionService _contentDefinitionService;
        private readonly ISchemaUpdateService _schemaUpdateService;
        //private readonly IFieldService _fieldService;
        private readonly IContentDefinitionManager _contentDefinitionManager;
        private readonly IContentDefinitionEditorEvents _contentDefinitionEditorEvents;
        private readonly IFieldService _fieldService;

        public SystemAdminController(IOrchardServices orchardServices
            ,IContentDefinitionService contentDefinitionService
            , ISchemaUpdateService schemaUpdateService
            //,IFieldService fieldService
            ,IContentDefinitionManager contentDefinitionManager
            ,IContentDefinitionEditorEvents contentDefinitionEditorEvents, 
            IFieldService fieldService) {
            Services = orchardServices;
            _contentDefinitionService = contentDefinitionService;
            _schemaUpdateService = schemaUpdateService;
            T = NullLocalizer.Instance;
            //_fieldService = fieldService;
            _contentDefinitionManager = contentDefinitionManager;
            _contentDefinitionEditorEvents = contentDefinitionEditorEvents;
            _fieldService = fieldService;
        }

        public IOrchardServices Services { get; private set; }
        public Localizer T { get; set; }
        public ILogger Logger { get; set; }

        public ActionResult List(string returnUrl) {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        public ActionResult Create() {
            //if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to create a content type.")))
            //    return new HttpUnauthorizedResult();

            var typeViewModel = _contentDefinitionService.GetType(string.Empty);

            return View(typeViewModel);
        }

        public ActionResult CreateChooseType(string id)
        {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content part.")))
                return new HttpUnauthorizedResult();

            var viewModel = new AddFieldViewModel
            {
                Fields = _contentDefinitionService.GetFields().OrderBy(x => x.FieldTypeName),
            };

            return View(viewModel);
        }

        public ActionResult EntityName(string displayName, int version) {
            return Json(new {
                result = _contentDefinitionService.GenerateContentTypeNameFromDisplayName(displayName),
                version = version
            });
        }

        public ActionResult CreateEditInfo(string id, string fieldTypeName)
        {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content part.")))
                return new HttpUnauthorizedResult();
            var contentFieldDefinition = new ContentFieldDefinition(fieldTypeName + "Create");
            var definition = new ContentPartFieldDefinition(contentFieldDefinition, string.Empty, new SettingsDictionary());
            var templates = _contentDefinitionEditorEvents.PartFieldEditor(definition);

            var viewModel = new AddFieldViewModel
            {
                FieldTypeName = fieldTypeName,
                TypeTemplates = templates,
                AddInLayout = true
            };

            return View(viewModel);
        }

        [HttpPost, ActionName("CreateEditInfo")]
        public ActionResult CreateEditInfoPost(string id, AddFieldViewModel viewModel)
        {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content part.")))
                return new HttpUnauthorizedResult();

            var partViewModel = _contentDefinitionService.GetPart(id);
            var typeViewModel = _contentDefinitionService.GetType(id);

            if (partViewModel == null)
            {
                // id passed in might be that of a type w/ no implicit field
                if (typeViewModel != null)
                {
                    partViewModel = new EditPartViewModel { Name = typeViewModel.Name };
                    _contentDefinitionService.AddPart(new CreatePartViewModel { Name = partViewModel.Name });
                    _contentDefinitionService.AddPartToType(partViewModel.Name, typeViewModel.Name);
                }
                else
                {
                    return HttpNotFound();
                }
            }

            viewModel.DisplayName = viewModel.DisplayName ?? String.Empty;
            viewModel.DisplayName = viewModel.DisplayName.Trim();
            viewModel.Name = (viewModel.Name ?? viewModel.DisplayName).ToSafeName();
            _fieldService.CreateCheck(id, viewModel, this);

            if (!ModelState.IsValid)
            {
                Services.TransactionManager.Cancel();
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                var temp = (from values in ModelState
                            from error in values.Value.Errors
                            select error.ErrorMessage).ToArray();
                return Content(string.Concat(temp));
            }
            _fieldService.Create(id, viewModel, this);

            Services.Notifier.Information(T("The \"{0}\" field has been added.", viewModel.DisplayName));
            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }

        public ActionResult FieldName(string displayName, int version)
        {
            return Json(new
            {
                result = _contentDefinitionService.GenerateContentTypeNameFromDisplayName(displayName),
                version = version
            });
        }

        [HttpPost, ActionName("Create")]
        public ActionResult CreatePOST(EditTypeViewModel viewModel) {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to create a content type.")))
                return new HttpUnauthorizedResult();

            viewModel.DisplayName = viewModel.DisplayName ?? String.Empty;
            viewModel.Name = (viewModel.Name ?? viewModel.DisplayName).ToSafeName();

            viewModel.FieldLabel = viewModel.FieldLabel ?? String.Empty;
            viewModel.FieldName = (viewModel.FieldName ?? viewModel.FieldLabel).ToSafeName();

            if (String.IsNullOrWhiteSpace(viewModel.DisplayName)) {
                ModelState.AddModelError("DisplayName", T("The Display Name name can't be empty.").ToString());
            }

            if (String.IsNullOrWhiteSpace(viewModel.Name)) {
                ModelState.AddModelError("Name", T("The Content Type Id can't be empty.").ToString());
            }

            if (String.IsNullOrWhiteSpace(viewModel.FieldLabel))
            {
                ModelState.AddModelError("DisplayName", T("The Field Label name can't be empty.").ToString());
            }

            if (String.IsNullOrWhiteSpace(viewModel.FieldName))
            {
                ModelState.AddModelError("Name", T("The Field Name can't be empty.").ToString());
            }

            if (!PluralizationService.CreateService(new CultureInfo("en-US")).IsSingular(viewModel.Name)) {
                ModelState.AddModelError("Name", T("The name should be singular.").ToString());
            }

            if (_contentDefinitionService.GetTypes().Any(t => String.Equals(t.Name.Trim(), viewModel.Name.Trim(), StringComparison.OrdinalIgnoreCase))) {
                ModelState.AddModelError("Name", T("A type with the same Id already exists.").ToString());
            }

            if (!String.IsNullOrWhiteSpace(viewModel.Name) && !viewModel.Name[0].IsLetter()) {
                ModelState.AddModelError("Name", T("The technical name must start with a letter.").ToString());
            }

            if (_contentDefinitionService.GetTypes().Any(t => String.Equals(t.DisplayName.Trim(), viewModel.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase))) {
                ModelState.AddModelError("DisplayName", T("A type with the same Display Name already exists.").ToString());
            }

            if (!PluralizationService.CreateService(new CultureInfo("en-US")).IsSingular(viewModel.FieldName))
            {
                ModelState.AddModelError("FieldName", T("The field name should be singular.").ToString());
            }

            if (!String.IsNullOrWhiteSpace(viewModel.FieldName) && !viewModel.FieldName[0].IsLetter())
            {
                ModelState.AddModelError("FieldName", T("The technical field name must start with a letter.").ToString());
            }

            if (!ModelState.IsValid) {
                Services.TransactionManager.Cancel();
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                var temp = (from values in ModelState
                            from error in values.Value.Errors
                             select error.ErrorMessage).ToArray();              
                return Content(string.Concat(temp));
            }

            _contentDefinitionService.AlterType(viewModel, this);

            _contentDefinitionService.AddPartToType(viewModel.Name, viewModel.Name);

            //var contentTypeDefinition = _contentDefinitionService.AddType(viewModel.DisplayName,viewModel.Name);

            // adds CommonPart by default
            //_contentDefinitionService.AddPartToType("CommonPart", viewModel.Name);

            //var typeViewModel = new EditTypeViewModel(contentTypeDefinition);


            Services.Notifier.Information(T("The \"{0}\" content type has been created.", viewModel.DisplayName));
            _schemaUpdateService.CreateTable(viewModel.Name.Trim());

            //AddFieldViewModel addViewModel = new AddFieldViewModel();
            //addViewModel.DisplayName = viewModel.FieldLabel.Trim();
            //addViewModel.Name = viewModel.FieldName.Trim();
            //addViewModel.FieldTypeName = "CoeveryTextField";
            //_fieldService.Create(viewModel.Name.Trim(), addViewModel, this);
            //var part = _contentDefinitionManager.GetPartDefinition(viewModel.Name.Trim());
            //var field = part.Fields.FirstOrDefault(x => x.Name == viewModel.FieldName);
            //if (field != null)
            //{
            //    field.Settings["CoeveryTextFieldSettings.IsDispalyField"] = bool.TrueString;
            //    field.Settings["CoeveryTextFieldSettings.Required"] = bool.TrueString;
            //    field.Settings["CoeveryTextFieldSettings.ReadOnly"] = bool.TrueString;
            //    field.Settings["CoeveryTextFieldSettings.AlwaysInLayout"] = bool.TrueString;
            //    field.Settings["CoeveryTextFieldSettings.IsSystemField"] = bool.TrueString;
            //}
            //_contentDefinitionManager.StorePartDefinition(part);

            //AddFieldViewModel addCreateByViewModel = new AddFieldViewModel();
            //addCreateByViewModel.DisplayName = "Created By";
            //addCreateByViewModel.Name = "CreatedBy";
            //addCreateByViewModel.FieldTypeName = "ReferenceField";
            //_fieldService.Create(viewModel.Name.Trim(), addCreateByViewModel, this);
            //var partCreateBy = _contentDefinitionManager.GetPartDefinition(viewModel.Name.Trim());
            //var fieldCreateBy = partCreateBy.Fields.FirstOrDefault(x => x.Name == viewModel.FieldName);
            //if (fieldCreateBy != null)
            //{
            //    fieldCreateBy.Settings["CoeveryTextFieldSettings.ContentTypeName"] = "";
            //    fieldCreateBy.Settings["CoeveryTextFieldSettings.Required"] = bool.TrueString;
            //    fieldCreateBy.Settings["CoeveryTextFieldSettings.ReadOnly"] = bool.TrueString;
            //    fieldCreateBy.Settings["CoeveryTextFieldSettings.AlwaysInLayout"] = bool.TrueString;
            //    fieldCreateBy.Settings["CoeveryTextFieldSettings.IsSystemField"] = bool.TrueString;
            //}
            //_contentDefinitionManager.StorePartDefinition(partCreateBy);

            //AddFieldViewModel addCreateDateViewModel = new AddFieldViewModel();
            //addCreateDateViewModel.DisplayName = "Create Date";
            //addCreateDateViewModel.Name = "CreateDate";
            //addCreateDateViewModel.FieldTypeName = "DatetimeField";
            //_fieldService.Create(viewModel.Name.Trim(), addCreateDateViewModel, this);
            //var partCreateDate = _contentDefinitionManager.GetPartDefinition(viewModel.Name.Trim());
            //var fieldCreateDate = partCreateDate.Fields.FirstOrDefault(x => x.Name == viewModel.FieldName);
            //if (fieldCreateDate != null)
            //{
            //    fieldCreateDate.Settings["CoeveryTextFieldSettings.DefaultValue"] = DateTime.Now.ToString();
            //    fieldCreateDate.Settings["CoeveryTextFieldSettings.Required"] = bool.TrueString;
            //    fieldCreateDate.Settings["CoeveryTextFieldSettings.ReadOnly"] = bool.TrueString;
            //    fieldCreateDate.Settings["CoeveryTextFieldSettings.AlwaysInLayout"] = bool.TrueString;
            //    fieldCreateDate.Settings["CoeveryTextFieldSettings.IsSystemField"] = bool.TrueString;
            //}
            //_contentDefinitionManager.StorePartDefinition(partCreateDate);

            //AddFieldViewModel addLastModifiedByViewModel = new AddFieldViewModel();
            //addLastModifiedByViewModel.DisplayName = "Last Modified By";
            //addLastModifiedByViewModel.Name = "LastModifiedBy";
            //addLastModifiedByViewModel.FieldTypeName = "ReferenceField";
            //_fieldService.Create(viewModel.Name.Trim(), addLastModifiedByViewModel, this);
            //var partLastModifiedBy = _contentDefinitionManager.GetPartDefinition(viewModel.Name.Trim());
            //var fieldLastModifiedBy = partLastModifiedBy.Fields.FirstOrDefault(x => x.Name == viewModel.FieldName);
            //if (fieldLastModifiedBy != null)
            //{
            //    fieldLastModifiedBy.Settings["CoeveryTextFieldSettings.ContentTypeName"] = "";
            //    fieldLastModifiedBy.Settings["CoeveryTextFieldSettings.Required"] = bool.TrueString;
            //    fieldLastModifiedBy.Settings["CoeveryTextFieldSettings.ReadOnly"] = bool.TrueString;
            //    fieldLastModifiedBy.Settings["CoeveryTextFieldSettings.AlwaysInLayout"] = bool.TrueString;
            //    fieldLastModifiedBy.Settings["CoeveryTextFieldSettings.IsSystemField"] = bool.TrueString;
            //}
            //_contentDefinitionManager.StorePartDefinition(partLastModifiedBy);

            //AddFieldViewModel addLastModifiedDateViewModel = new AddFieldViewModel();
            //addLastModifiedDateViewModel.DisplayName = "Last Modified Date";
            //addLastModifiedDateViewModel.Name = "LastModifiedDate";
            //addLastModifiedDateViewModel.FieldTypeName = "DatetimeField";
            //_fieldService.Create(viewModel.Name.Trim(), addLastModifiedDateViewModel, this);
            //var partLastModifiedDate = _contentDefinitionManager.GetPartDefinition(viewModel.Name.Trim());
            //var fieldLastModifiedDate = partLastModifiedDate.Fields.FirstOrDefault(x => x.Name == viewModel.FieldName);
            //if (fieldLastModifiedDate != null)
            //{
            //    fieldLastModifiedDate.Settings["CoeveryTextFieldSettings.DefaultValue"] = DateTime.Now.ToString();
            //    fieldLastModifiedDate.Settings["CoeveryTextFieldSettings.Required"] = bool.TrueString;
            //    fieldLastModifiedDate.Settings["CoeveryTextFieldSettings.ReadOnly"] = bool.TrueString;
            //    fieldLastModifiedDate.Settings["CoeveryTextFieldSettings.AlwaysInLayout"] = bool.TrueString;
            //    fieldLastModifiedDate.Settings["CoeveryTextFieldSettings.IsSystemField"] = bool.TrueString;
            //}
            //_contentDefinitionManager.StorePartDefinition(partLastModifiedDate);

            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }

        public ActionResult EditFields(string id, string fieldName)
        {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content type.")))
                return new HttpUnauthorizedResult();

            var typeViewModel = _contentDefinitionService.GetType(id);
            if (typeViewModel == null)
            {
                return HttpNotFound();
            }

            var fieldViewModel = typeViewModel.Fields.FirstOrDefault(x => x.Name == fieldName);

            if (fieldViewModel == null)
            {
                return HttpNotFound();
            }

            return View(fieldViewModel);
        }

        [HttpPost, ActionName("Edit")]
        public ActionResult EditPost(EditPartFieldViewModel viewModel, string id)
        {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content type.")))
                return new HttpUnauthorizedResult();

            if (viewModel == null)
                return HttpNotFound();

            var partViewModel = _contentDefinitionService.GetPart(id);
            if (partViewModel == null)
            {
                return HttpNotFound();
            }

            // prevent null reference exception in validation
            viewModel.DisplayName = viewModel.DisplayName ?? String.Empty;

            // remove extra spaces
            viewModel.DisplayName = viewModel.DisplayName.Trim();

            if (String.IsNullOrWhiteSpace(viewModel.DisplayName))
            {
                ModelState.AddModelError("DisplayName", T("The Display Name name can't be empty.").ToString());
            }

            if (partViewModel.Fields.Any(t => t.Name != viewModel.Name && String.Equals(t.DisplayName.Trim(), viewModel.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError("DisplayName", T("A field with the same Display Name already exists.").ToString());
            }

            var fieldDefinition = _contentDefinitionManager.GetPartDefinition(id).Fields.FirstOrDefault(f => f.Name == viewModel.Name);

            if (fieldDefinition == null)
            {
                return HttpNotFound();
            }

            var typeViewModel = _contentDefinitionService.GetType(id);
            var field = typeViewModel.Fields.FirstOrDefault(f => f.Name == viewModel.Name);
            CheckData(field);
            if (!ModelState.IsValid)
            {
                Services.TransactionManager.Cancel();
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                var temp = (from values in ModelState
                            from error in values.Value.Errors
                            select error.ErrorMessage).ToArray();
                return Content(string.Concat(temp));
            }

            fieldDefinition.DisplayName = viewModel.DisplayName;
            _contentDefinitionManager.StorePartDefinition(partViewModel._Definition);

            _contentDefinitionService.AlterField(id, viewModel, this);
            if (!ModelState.IsValid)
            {
                Services.TransactionManager.Cancel();
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                var temp = (from values in ModelState
                            from error in values.Value.Errors
                            select error.ErrorMessage).ToArray();
                return Content(string.Concat(temp));
            }

            _schemaUpdateService.CreateColumn(partViewModel.Name, field.Name, field.FieldDefinition.Name);
            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }

        public ActionResult Edit(string id) {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content type.")))
                return new HttpUnauthorizedResult();

            var typeViewModel = _contentDefinitionService.GetType(id);

            if (typeViewModel == null)
                return HttpNotFound();

            return View(typeViewModel);
        }

        [HttpPost, ActionName("Edit")]
        [FormValueRequired("submit.Delete")]
        public ActionResult Delete(string id) {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to delete a content type.")))
                return new HttpUnauthorizedResult();

            var typeViewModel = _contentDefinitionService.GetType(id);

            if (typeViewModel == null)
                return HttpNotFound();

            _contentDefinitionService.RemoveType(id, true);

            Services.Notifier.Information(T("\"{0}\" has been removed.", typeViewModel.DisplayName));

            return RedirectToAction("List");
        }

        public ActionResult Detail(string id) {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content type.")))
                return new HttpUnauthorizedResult();

            var typeViewModel = _contentDefinitionService.GetType(id);

            if (typeViewModel == null)
                return HttpNotFound();

            return View(typeViewModel);
        }

        public ActionResult Fields() {
            return View();
        }

        public ActionResult Relationships() {
            return View();
        }

        bool IUpdateModel.TryUpdateModel<TModel>(TModel model, string prefix, string[] includeProperties, string[] excludeProperties) {
            return base.TryUpdateModel(model, prefix, includeProperties, excludeProperties);
        }

        void IUpdateModel.AddModelError(string key, LocalizedString errorMessage) {
            ModelState.AddModelError(key, errorMessage.ToString());
        }

        public ActionResult Items(string id, string fieldName)
        {
            return View();
        }

        public ActionResult DependencyList(string id)
        {
            return View();
        }

        public ActionResult CreateDependency(string id)
        {
            var typeViewModel = _contentDefinitionService.GetType(id);
            var controlFields = new List<EditPartFieldViewModel>();
            var dependentFields = new List<EditPartFieldViewModel>();
            foreach (var field in typeViewModel.Fields)
            {
                switch (field.FieldDefinition.Name)
                {
                    case "SelectField":
                        controlFields.Add(field);
                        dependentFields.Add(field);
                        break;
                    case "BooleanField":
                        controlFields.Add(field);
                        break;
                }
            }
            var viewModel = new FieldDependencyViewModel
            {
                ControlFields = controlFields,
                DependentFields = dependentFields
            };
            return View(viewModel);
        }

        public ActionResult EditDependency(string entityName, int itemId)
        {
            var typeViewModel = _contentDefinitionService.GetType(entityName);

            return View();
        }

        private void CheckData(EditPartFieldViewModel serverField)
        {
            var settingsStr = serverField.FieldDefinition.Name + "Settings";
            var clientSettings = new FieldSettings();
            TryUpdateModel(clientSettings, settingsStr);
            clientSettings.ReadOnly = false;

            var serverSettings = new FieldSettings
            {
                IsSystemField = bool.Parse(serverField.Settings[settingsStr + ".IsSystemField"]),
                Required = bool.Parse(serverField.Settings[settingsStr + ".Required"]),
                ReadOnly = bool.Parse(serverField.Settings[settingsStr + ".ReadOnly"]),
                AlwaysInLayout = bool.Parse(serverField.Settings[settingsStr + ".AlwaysInLayout"])
            };

            if (clientSettings.ReadOnly)
            {
                ModelState.AddModelError("ReadOnly", T("Can't modify the ReadOnly field.").ToString());
            }

            if (clientSettings.IsSystemField != serverSettings.IsSystemField)
            {
                ModelState.AddModelError("IsSystemField", T("Can't modify the IsSystemField field.").ToString());
            }

            if (serverSettings.IsSystemField)
            {
                if (clientSettings.Required != serverSettings.Required)
                {
                    ModelState.AddModelError("Required", T("Can't modify the Required field.").ToString());
                }
                if (clientSettings.ReadOnly != serverSettings.ReadOnly)
                {
                    ModelState.AddModelError("ReadOnly", T("Can't modify the ReadOnly field.").ToString());
                }
                if (clientSettings.AlwaysInLayout != serverSettings.AlwaysInLayout)
                {
                    ModelState.AddModelError("AlwaysInLayout", T("Can't modify the AlwaysInLayout field.").ToString());
                }
            }
        }
    }
}