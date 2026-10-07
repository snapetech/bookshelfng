import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { metadataProfileNames } from 'Helpers/Props';
import createSortedSectionSelector from 'Store/Selectors/createSortedSectionSelector';
import sortByName from 'Utilities/Array/sortByName';
import translate from 'Utilities/String/translate';
import SelectInput from './SelectInput';

function createMapStateToProps() {
  return createSelector(
    createSortedSectionSelector('settings.metadataProfiles', sortByName),
    (state, { includeNoChange }) => includeNoChange,
    (state, { includeNoChangeDisabled }) => includeNoChangeDisabled,
    (state, { includeMixed }) => includeMixed,
    (state, { includeNone }) => includeNone,
    (state, { includeInherit }) => includeInherit,
    (metadataProfiles, includeNoChange, includeNoChangeDisabled = true, includeMixed, includeNone, includeInherit) => {

      const profiles = metadataProfiles.items.filter((item) => item.name !== metadataProfileNames.NONE);
      const noneProfile = metadataProfiles.items.find((item) => item.name === metadataProfileNames.NONE);

      const values = _.map(profiles, (metadataProfile) => {
        return {
          key: metadataProfile.id,
          value: metadataProfile.name
        };
      });

      if (includeInherit) {
        values.unshift({ key: 'inherit', value: 'Use author default' });
      }

      if (includeNone) {
        values.push({
          key: noneProfile.id,
          value: noneProfile.name
        });
      }

      if (includeNoChange) {
        values.unshift({
          key: 'noChange',
          value: translate('NoChange'),
          isDisabled: includeNoChangeDisabled
        });
      }

      if (includeMixed) {
        values.unshift({
          key: 'mixed',
          value: '(Mixed)',
          isDisabled: true
        });
      }

      return {
        values
      };
    }
  );
}

class MetadataProfileSelectInputConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    const {
      name,
      value,
      values,
      includeInherit
    } = this.props;

    if (includeInherit && (value == null || value === 0)) {
      return;
    }

    if (!value || !values.some((option) => option.key === value || parseInt(option.key) === value)) {
      const firstValue = values.find((option) => !isNaN(parseInt(option.key)));

      if (firstValue) {
        this.onChange({ name, value: firstValue.key });
      }
    }
  }

  //
  // Listeners

  onChange = ({ name, value }) => {
    let nextValue = value;

    if (value === 'inherit') {
      nextValue = 0;
    } else if (value !== 'noChange') {
      nextValue = parseInt(value);
    }

    this.props.onChange({ name, value: nextValue });
  };

  //
  // Render

  render() {
    return (
      <SelectInput
        {...this.props}
        value={this.props.includeInherit && (this.props.value == null || this.props.value === 0) ? 'inherit' : this.props.value}
        onChange={this.onChange}
      />
    );
  }
}

MetadataProfileSelectInputConnector.propTypes = {
  name: PropTypes.string.isRequired,
  value: PropTypes.oneOfType([PropTypes.number, PropTypes.string]),
  values: PropTypes.arrayOf(PropTypes.object).isRequired,
  includeNoChange: PropTypes.bool.isRequired,
  includeNone: PropTypes.bool.isRequired,
  includeInherit: PropTypes.bool.isRequired,
  onChange: PropTypes.func.isRequired
};

MetadataProfileSelectInputConnector.defaultProps = {
  includeNoChange: false,
  includeNone: true,
  includeInherit: false
};

export default connect(createMapStateToProps)(MetadataProfileSelectInputConnector);
